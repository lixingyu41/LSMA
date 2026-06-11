using System.IO.Compression;
using System.Text.Json;
using LSMA.Models;
using LSMA.Utilities;

namespace LSMA.Services;

public sealed class ModPackService(
    AppStateService state,
    GameRunLockService runLock,
    ModScannerService scanner,
    SettingsService settings,
    NexusClient nexus,
    NexusDownloadService downloads,
    ExternalArchiveReader externalArchiveReader,
    FileSystemSafeService files,
    LoggingService logging)
{
    private const string PackageManifestName = "lsma-modpack.json";
    private const string InitialPackName = "当前组合";

    public async Task<ModPackCatalog> LoadAsync()
    {
        var catalog = await LoadCatalogCoreAsync();
        ApplySettingsNexusBindings(catalog);
        ApplyActiveFlag(catalog);
        return catalog;
    }

    public async Task<ModPackCatalog> EnsureInitializedAsync()
    {
        var catalog = await LoadCatalogCoreAsync();
        Directory.CreateDirectory(AppPaths.ModPacks);
        if (!catalog.InitialPackCreated)
        {
            var pack = CreatePack(InitialPackName);
            pack.Entries = await ReadCurrentModEntriesAsync();
            catalog.Packs.Add(pack);
            catalog.ActivePackId = pack.Id;
            catalog.InitialPackCreated = true;
            await SaveCatalogAsync(catalog);
        }
        else
        {
            EnsureCatalogHasActivePack(catalog);
            await SyncActivePackFromCurrentModsAsync(catalog);
            ApplySettingsNexusBindings(catalog);
            await SaveCatalogAsync(catalog);
        }

        ApplyActiveFlag(catalog);
        return catalog;
    }

    public async Task<ModPackCatalog> CreateEmptyPackAsync(string? name = null)
    {
        var catalog = await EnsureInitializedAsync();
        var packName = string.IsNullOrWhiteSpace(name)
            ? NextPackName(catalog, "新模组包")
            : UniquePackName(catalog, NormalizePackName(name));
        var pack = CreatePack(packName);
        catalog.Packs.Add(pack);
        await SaveCatalogAsync(catalog);
        ApplyActiveFlag(catalog);
        return catalog;
    }

    public async Task<ModPackCatalog> RenameAsync(string packId, string name)
    {
        var catalog = await EnsureInitializedAsync();
        var pack = RequirePack(catalog, packId);
        pack.Name = UniquePackName(catalog, NormalizePackName(name), pack.Id);
        pack.UpdatedAt = DateTime.Now;
        await SaveCatalogAsync(catalog);
        ApplyActiveFlag(catalog);
        return catalog;
    }

    public async Task<ModPackCatalog> CaptureCurrentModsAsync(string packId)
    {
        var catalog = await EnsureInitializedAsync();
        var pack = RequirePack(catalog, packId);
        EnsureGameNotRunning();
        var entries = await ReadCurrentModEntriesAsync();
        if (!IsActive(catalog, pack))
        {
            var packModsRoot = GetPackModsRoot(pack);
            await ReplaceDirectoryWithCurrentModsAsync(packModsRoot, GetPackRoot(pack));
        }

        pack.Entries = entries;
        pack.UpdatedAt = DateTime.Now;
        await SaveCatalogAsync(catalog);
        ApplyActiveFlag(catalog);
        return catalog;
    }

    public async Task<ModPackCatalog> SwitchAsync(string targetPackId)
    {
        var catalog = await EnsureInitializedAsync();
        var target = RequirePack(catalog, targetPackId);
        if (target.MissingCount > 0)
        {
            throw new InvalidOperationException("目标模组包存在缺失文件，不能切换。");
        }

        if (catalog.ActivePackId == target.Id)
        {
            return catalog;
        }

        EnsureGameNotRunning();
        var current = catalog.Packs.FirstOrDefault(pack => pack.Id == catalog.ActivePackId)
            ?? throw new InvalidOperationException("当前模组包不存在。");
        var gameMods = GetGameModsRoot();
        var currentStorage = GetPackModsRoot(current);
        var targetStorage = GetPackModsRoot(target);
        Directory.CreateDirectory(gameMods);
        Directory.CreateDirectory(currentStorage);
        Directory.CreateDirectory(targetStorage);

        var currentMoved = new List<(string Source, string Destination)>();
        var targetMoved = new List<(string Source, string Destination)>();
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(gameMods).ToList())
            {
                var destination = Path.Combine(currentStorage, Path.GetFileName(directory));
                if (Directory.Exists(destination))
                {
                    throw new IOException($"当前包仓库已存在同名模组目录：{Path.GetFileName(directory)}。");
                }

                await files.MoveDirectoryAsync(directory, destination, currentStorage);
                currentMoved.Add((directory, destination));
            }

            foreach (var entry in target.Entries)
            {
                var source = Path.Combine(targetStorage, entry.FolderName);
                if (!Directory.Exists(source))
                {
                    entry.IsMissing = true;
                    entry.MissingReason = "包仓库缺少模组目录";
                    throw new InvalidDataException($"{entry.Name} 缺少模组文件。");
                }

                var destination = Path.Combine(gameMods, entry.FolderName);
                if (Directory.Exists(destination))
                {
                    throw new IOException($"游戏 Mods 已存在同名模组目录：{entry.FolderName}。");
                }

                await files.MoveDirectoryAsync(source, destination, gameMods);
                targetMoved.Add((source, destination));
            }

            current.Entries = await ReadEntriesFromDirectoryAsync(currentStorage, enabled: false);
            foreach (var entry in target.Entries)
            {
                entry.IsMissing = false;
                entry.MissingReason = null;
            }

            catalog.ActivePackId = target.Id;
            current.UpdatedAt = DateTime.Now;
            target.UpdatedAt = DateTime.Now;
            await SaveCatalogAsync(catalog);
            await logging.InfoAsync($"已切换模组包：{target.Name}");
        }
        catch (Exception exception)
        {
            await RollbackSwitchAsync(targetMoved, currentMoved, gameMods, targetStorage, currentStorage);
            await SaveCatalogAsync(catalog);
            await logging.ErrorAsync("切换模组包失败", exception);
            throw;
        }

        ApplyActiveFlag(catalog);
        return catalog;
    }

    public async Task<ModPackCatalog> ImportAsync(string packagePath, string? mergeIntoPackId, string? apiKey)
    {
        var catalog = await EnsureInitializedAsync();
        EnsureGameNotRunning();
        var preparedPackagePath = await PrepareImportPackageAsync(packagePath);
        var cleanupPreparedPackage = !preparedPackagePath.Equals(packagePath, StringComparison.OrdinalIgnoreCase);
        try
        {
            var imported = await ReadPackageManifestAsync(preparedPackagePath);
            var target = mergeIntoPackId is null
                ? CreatePack(string.IsNullOrWhiteSpace(imported.Pack.Name) ? NextPackName(catalog, "导入模组包") : UniquePackName(catalog, imported.Pack.Name))
                : RequirePack(catalog, mergeIntoPackId);

            if (mergeIntoPackId is null)
            {
                catalog.Packs.Add(target);
            }

            using var archive = ZipFile.OpenRead(preparedPackagePath);
            ValidateArchivePaths(archive);
            foreach (var incoming in imported.Pack.Entries)
            {
                await MergeEntryAsync(catalog, target, incoming, archive, imported.ArchiveRoot);
            }

            target.UpdatedAt = DateTime.Now;
            await SaveCatalogAsync(catalog);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                await DownloadMissingAsync(target.Id, apiKey);
                catalog = await LoadCatalogCoreAsync();
            }
        }
        finally
        {
            if (cleanupPreparedPackage && File.Exists(preparedPackagePath))
            {
                files.EnsureInside(preparedPackagePath, AppPaths.Temp);
                File.Delete(preparedPackagePath);
            }
        }

        ApplyActiveFlag(catalog);
        return catalog;
    }

    public async Task<ModPackCatalog> DownloadMissingAsync(string packId, string apiKey)
    {
        var catalog = await EnsureInitializedAsync();
        var pack = RequirePack(catalog, packId);
        EnsureGameNotRunning();
        foreach (var entry in pack.Entries.Where(entry => entry.IsMissing).ToList())
        {
            if (entry.NexusModId is null)
            {
                entry.MissingReason = "缺少 Nexus ID";
                continue;
            }

            try
            {
                var file = await FindMatchingNexusFileAsync(entry, apiKey);
                if (file is null)
                {
                    entry.MissingReason = "Nexus 未找到匹配版本文件";
                    continue;
                }

                var item = new DownloadQueueItem
                {
                    ModId = entry.NexusModId.Value,
                    FileId = file.FileId,
                    ModName = entry.Name,
                    FileName = file.FileName ?? file.Name ?? string.Empty
                };
                var packagePath = await downloads.DownloadAsync(item, apiKey);
                await InstallDownloadedEntryAsync(catalog, pack, entry, packagePath);
            }
            catch (Exception exception)
            {
                entry.IsMissing = true;
                entry.MissingReason = exception.Message;
                await logging.ErrorAsync($"下载模组包缺失项失败：{entry.Name}", exception);
            }
        }

        pack.UpdatedAt = DateTime.Now;
        await SaveCatalogAsync(catalog);
        ApplyActiveFlag(catalog);
        return catalog;
    }

    public async Task ExportAsync(string packId, string destinationPath, ModPackExportOptions options)
    {
        var catalog = await EnsureInitializedAsync();
        var pack = RequirePack(catalog, packId);
        if (IsActive(catalog, pack))
        {
            pack.Entries = await ReadCurrentModEntriesAsync();
            pack.UpdatedAt = DateTime.Now;
            await SaveCatalogAsync(catalog);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var temporary = destinationPath + ".partial";
        if (File.Exists(temporary))
        {
            File.Delete(temporary);
        }

        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                var manifestEntry = archive.CreateEntry(PackageManifestName, CompressionLevel.Fastest);
                await using (var output = manifestEntry.Open())
                {
                    await JsonSerializer.SerializeAsync(output, CreatePortablePack(pack), JsonHelper.Options);
                }

                if (options.IncludeModFiles)
                {
                    var sourceRoot = IsActive(catalog, pack) ? GetGameModsRoot() : GetPackModsRoot(pack);
                    foreach (var entry in pack.Entries)
                    {
                        var source = Path.Combine(sourceRoot, entry.FolderName);
                        if (!Directory.Exists(source))
                        {
                            throw new InvalidDataException($"{entry.Name} 缺少模组文件，不能导出带文件包。");
                        }

                        AddDirectoryToArchive(archive, source, $"mods/{entry.FolderName}");
                    }
                }
            }

            File.Move(temporary, destinationPath, true);
            await logging.InfoAsync($"已导出模组包：{pack.Name}");
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public async Task<ModPackCatalog> DeleteAsync(string packId)
    {
        var catalog = await EnsureInitializedAsync();
        var pack = RequirePack(catalog, packId);
        if (IsActive(catalog, pack))
        {
            throw new InvalidOperationException("不能删除当前正在加载的模组包。");
        }

        var root = GetPackRoot(pack);
        if (Directory.Exists(root))
        {
            await files.DeleteDirectoryAsync(root, AppPaths.ModPacks);
        }

        catalog.Packs.Remove(pack);
        await SaveCatalogAsync(catalog);
        ApplyActiveFlag(catalog);
        return catalog;
    }

    private async Task MergeEntryAsync(ModPackCatalog catalog, ModPackInfo target, ModPackEntry incoming, ZipArchive archive, string packageRoot)
    {
        NormalizeEntry(incoming);
        var active = IsActive(catalog, target);
        var targetRoot = active ? GetGameModsRoot() : GetPackModsRoot(target);
        var existing = target.Entries.FirstOrDefault(entry =>
            entry.UniqueId.Equals(incoming.UniqueId, StringComparison.OrdinalIgnoreCase));
        var hasFiles = PackageContainsEntryFiles(archive, incoming, packageRoot);
        if (existing is not null)
        {
            target.Entries.Remove(existing);
            if (hasFiles)
            {
                var existingPath = Path.Combine(targetRoot, existing.FolderName);
                if (Directory.Exists(existingPath))
                {
                    await files.DeleteDirectoryAsync(existingPath, targetRoot);
                }
            }
        }

        if (hasFiles)
        {
            if (Directory.Exists(Path.Combine(targetRoot, incoming.FolderName)))
            {
                throw new IOException($"目标位置已存在同名模组目录：{incoming.FolderName}。");
            }

            await ExtractEntryFromPackageAsync(archive, incoming, targetRoot, packageRoot);
            incoming.IsMissing = false;
            incoming.MissingReason = null;
        }
        else
        {
            incoming.IsMissing = true;
            incoming.MissingReason = "导入包不含模组文件";
        }

        target.Entries.Add(incoming);
    }

    private async Task InstallDownloadedEntryAsync(ModPackCatalog catalog, ModPackInfo pack, ModPackEntry entry, string packagePath)
    {
        var prepared = packagePath;
        var cleanup = false;
        if (!Path.GetExtension(packagePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            prepared = await externalArchiveReader.ConvertToZipAsync(packagePath);
            cleanup = true;
        }

        try
        {
            using var archive = ZipFile.OpenRead(prepared);
            ValidateArchivePaths(archive);
            var packaged = await FindPackagedEntryAsync(archive, entry.UniqueId);
            if (packaged is null)
            {
                throw new InvalidDataException("下载文件未包含匹配的模组信息。");
            }

            var targetRoot = IsActive(catalog, pack) ? GetGameModsRoot() : GetPackModsRoot(pack);
            var previousTarget = Path.Combine(targetRoot, entry.FolderName);
            var target = Path.Combine(targetRoot, packaged.Entry.FolderName);
            if (!previousTarget.Equals(target, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(previousTarget))
            {
                await files.DeleteDirectoryAsync(previousTarget, targetRoot);
            }

            if (Directory.Exists(target))
            {
                await files.DeleteDirectoryAsync(target, targetRoot);
            }

            await ExtractArchiveRootAsync(archive, packaged.ArchiveRoot, target);
            entry.Name = packaged.Entry.Name;
            entry.Version = packaged.Entry.Version;
            entry.FolderName = packaged.Entry.FolderName;
            entry.NexusModId ??= packaged.Entry.NexusModId;
            entry.IsMissing = false;
            entry.MissingReason = null;
        }
        finally
        {
            if (cleanup && File.Exists(prepared))
            {
                files.EnsureInside(prepared, AppPaths.Temp);
                File.Delete(prepared);
            }
        }
    }

    private async Task<NexusFileInfo?> FindMatchingNexusFileAsync(ModPackEntry entry, string apiKey)
    {
        var files = await nexus.GetFilesAsync(entry.NexusModId!.Value, apiKey);
        return files
            .Where(file => string.Equals(file.CategoryName, "MAIN", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(file => string.Equals(file.Version, entry.Version, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> PrepareImportPackageAsync(string packagePath)
    {
        if (Directory.Exists(packagePath))
        {
            var preparedPackagePath = Path.Combine(AppPaths.Temp, $"modpack_folder_{Guid.NewGuid():N}.zip");
            Directory.CreateDirectory(AppPaths.Temp);
            try
            {
                await Task.Run(() =>
                {
                    if (!File.Exists(Path.Combine(packagePath, PackageManifestName)))
                    {
                        throw new InvalidDataException("整合包文件夹缺少 lsma-modpack.json。");
                    }

                    ZipFile.CreateFromDirectory(
                        packagePath,
                        preparedPackagePath,
                        CompressionLevel.Fastest,
                        includeBaseDirectory: false);
                });
                await logging.InfoAsync($"已读取拖入的整合包文件夹：{packagePath}");
                return preparedPackagePath;
            }
            catch
            {
                if (File.Exists(preparedPackagePath))
                {
                    File.Delete(preparedPackagePath);
                }

                throw;
            }
        }

        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException("找不到模组包文件。", packagePath);
        }

        return packagePath;
    }

    private async Task<ImportedModPackPackage> ReadPackageManifestAsync(string packagePath)
    {
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException("找不到模组包文件。", packagePath);
        }

        using var archive = ZipFile.OpenRead(packagePath);
        ValidateArchivePaths(archive);
        var entry = archive.GetEntry(PackageManifestName)
            ?? archive.Entries.FirstOrDefault(item =>
                Path.GetFileName(item.FullName.Replace('\\', '/')).Equals(PackageManifestName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("模组包缺少 lsma-modpack.json。");
        await using var stream = entry.Open();
        var pack = await JsonSerializer.DeserializeAsync<ModPackInfo>(stream, JsonHelper.Options)
            ?? throw new InvalidDataException("模组包信息无效。");
        foreach (var mod in pack.Entries)
        {
            NormalizeEntry(mod);
        }

        var archiveRoot = Path.GetDirectoryName(entry.FullName.Replace('\\', '/'))?.Replace('\\', '/') ?? string.Empty;
        return new ImportedModPackPackage(pack, archiveRoot);
    }

    private async Task<PackagedEntry?> FindPackagedEntryAsync(ZipArchive archive, string uniqueId)
    {
        foreach (var manifest in archive.Entries.Where(entry =>
                     entry.FullName.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)))
        {
            await using var stream = manifest.Open();
            var modManifest = await JsonSerializer.DeserializeAsync<ModManifest>(stream, JsonHelper.Options);
            if (modManifest?.UniqueID?.Equals(uniqueId, StringComparison.OrdinalIgnoreCase) != true)
            {
                continue;
            }

            var folderName = FirstArchiveFolder(manifest.FullName)
                ?? FileSystemHelper.SafeFilePart(modManifest.Name ?? modManifest.UniqueID ?? uniqueId);
            return new PackagedEntry(
                Path.GetDirectoryName(manifest.FullName)?.Replace('\\', '/') ?? string.Empty,
                EntryFromManifest(folderName, modManifest));
        }

        return null;
    }

    private async Task ExtractEntryFromPackageAsync(ZipArchive archive, ModPackEntry entry, string targetRoot, string packageRoot)
    {
        var archiveRoot = CombineArchivePath(packageRoot, $"mods/{entry.FolderName}");
        var target = Path.Combine(targetRoot, entry.FolderName);
        await ExtractArchiveRootAsync(archive, archiveRoot, target);
    }

    private async Task ExtractArchiveRootAsync(ZipArchive archive, string archiveRoot, string destination)
    {
        var targetRoot = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var parentRoot = Path.GetDirectoryName(destination)!;
        files.EnsureInside(destination, parentRoot);
        Directory.CreateDirectory(destination);
        var prefix = string.IsNullOrWhiteSpace(archiveRoot) ? string.Empty : archiveRoot.TrimEnd('/') + "/";
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = entry.FullName[prefix.Length..];
            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            var target = Path.GetFullPath(Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("模组包包含不安全路径。");
            }

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = File.Create(target);
            await input.CopyToAsync(output);
        }
    }

    private bool PackageContainsEntryFiles(ZipArchive archive, ModPackEntry entry, string packageRoot)
    {
        var prefix = CombineArchivePath(packageRoot, $"mods/{entry.FolderName.TrimEnd('/')}/");
        return archive.Entries.Any(item =>
            item.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && !item.FullName.EndsWith("/", StringComparison.Ordinal));
    }

    private async Task ReplaceDirectoryWithCurrentModsAsync(string destinationRoot, string permittedRoot)
    {
        if (Directory.Exists(destinationRoot))
        {
            await files.DeleteDirectoryAsync(destinationRoot, permittedRoot);
        }

        Directory.CreateDirectory(destinationRoot);
        foreach (var directory in Directory.EnumerateDirectories(GetGameModsRoot()))
        {
            var target = Path.Combine(destinationRoot, Path.GetFileName(directory));
            await CopyDirectoryAsync(directory, target, destinationRoot);
        }
    }

    private async Task CopyDirectoryAsync(string source, string destination, string permittedRoot)
    {
        files.EnsureInside(destination, permittedRoot);
        await Task.Run(() =>
        {
            Directory.CreateDirectory(destination);
            foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, directory);
                Directory.CreateDirectory(Path.Combine(destination, relative));
            }

            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                var target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
        });
    }

    private async Task<List<ModPackEntry>> ReadCurrentModEntriesAsync()
    {
        return await ReadEntriesFromDirectoryAsync(GetGameModsRoot(), enabled: true);
    }

    private async Task SyncActivePackFromCurrentModsAsync(ModPackCatalog catalog)
    {
        if (state.GameDirectory is null || catalog.ActivePackId is null)
        {
            return;
        }

        var active = catalog.Packs.FirstOrDefault(pack => pack.Id == catalog.ActivePackId);
        if (active is null)
        {
            return;
        }

        var currentEntries = await ReadCurrentModEntriesAsync();
        if (EntriesMatch(active.Entries, currentEntries))
        {
            return;
        }

        active.Entries = currentEntries;
        active.UpdatedAt = DateTime.Now;
    }

    private async Task<List<ModPackEntry>> ReadEntriesFromDirectoryAsync(string root, bool enabled)
    {
        var entries = new List<ModPackEntry>();
        if (!Directory.Exists(root))
        {
            return entries;
        }

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var info = await scanner.ScanDirectoryAsync(directory, enabled);
            entries.Add(EntryFromMod(info));
        }

        return entries
            .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private ModPackEntry EntryFromMod(ModInfo mod)
    {
        var entry = EntryFromManifest(mod.FolderName, mod.Manifest);
        entry.UniqueId = string.IsNullOrWhiteSpace(entry.UniqueId) ? $"folder:{mod.FolderName}" : entry.UniqueId;
        entry.NexusModId = mod.NexusModId ?? GetNexusBinding(mod.Manifest?.UniqueID);
        return entry;
    }

    private bool ApplySettingsNexusBindings(ModPackCatalog catalog)
    {
        var changed = false;
        foreach (var entry in catalog.Packs.SelectMany(pack => pack.Entries))
        {
            if (entry.NexusModId is null && GetNexusBinding(entry.UniqueId) is { } binding)
            {
                entry.NexusModId = binding;
                changed = true;
            }
        }

        return changed;
    }

    private long? GetNexusBinding(string? uniqueId)
    {
        return !string.IsNullOrWhiteSpace(uniqueId)
            && !uniqueId.StartsWith("folder:", StringComparison.OrdinalIgnoreCase)
            && settings.Current.NexusBindings.TryGetValue(uniqueId, out var binding)
                ? binding
                : null;
    }

    private static bool EntriesMatch(IReadOnlyList<ModPackEntry> left, IReadOnlyList<ModPackEntry> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        return left
            .OrderBy(entry => entry.FolderName, StringComparer.OrdinalIgnoreCase)
            .Zip(right.OrderBy(entry => entry.FolderName, StringComparer.OrdinalIgnoreCase))
            .All(pair => EntryMatches(pair.First, pair.Second));
    }

    private static bool EntryMatches(ModPackEntry left, ModPackEntry right)
    {
        return string.Equals(left.UniqueId, right.UniqueId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            && string.Equals(left.Version, right.Version, StringComparison.Ordinal)
            && left.NexusModId == right.NexusModId
            && string.Equals(left.FolderName, right.FolderName, StringComparison.OrdinalIgnoreCase)
            && left.IsMissing == right.IsMissing
            && string.Equals(left.MissingReason, right.MissingReason, StringComparison.Ordinal);
    }

    private static ModPackEntry EntryFromManifest(string folderName, ModManifest? manifest)
    {
        return new ModPackEntry
        {
            UniqueId = manifest?.UniqueID ?? string.Empty,
            Name = manifest?.Name ?? folderName,
            Version = manifest?.Version ?? "-",
            NexusModId = GetNexusModId(manifest?.UpdateKeys),
            FolderName = folderName,
            IsMissing = false
        };
    }

    private static long? GetNexusModId(IEnumerable<string>? updateKeys)
    {
        foreach (var key in updateKeys ?? [])
        {
            if (key.StartsWith("Nexus:", StringComparison.OrdinalIgnoreCase)
                && long.TryParse(key["Nexus:".Length..], out var id))
            {
                return id;
            }
        }

        return null;
    }

    private async Task<ModPackCatalog> LoadCatalogCoreAsync()
    {
        try
        {
            if (File.Exists(AppPaths.ModPackCatalogFile))
            {
                return await JsonHelper.ReadAsync<ModPackCatalog>(AppPaths.ModPackCatalogFile) ?? new ModPackCatalog();
            }
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync("读取模组包目录失败", exception);
        }

        return new ModPackCatalog();
    }

    private async Task SaveCatalogAsync(ModPackCatalog catalog)
    {
        ApplyActiveFlag(catalog);
        Directory.CreateDirectory(AppPaths.ModPacks);
        foreach (var pack in catalog.Packs)
        {
            Directory.CreateDirectory(GetPackModsRoot(pack));
            await JsonHelper.WriteAsync(GetPackManifestPath(pack), pack);
        }

        await JsonHelper.WriteAsync(AppPaths.ModPackCatalogFile, catalog);
    }

    private static void EnsureCatalogHasActivePack(ModPackCatalog catalog)
    {
        if (catalog.Packs.Count == 0)
        {
            return;
        }

        if (catalog.ActivePackId is null || catalog.Packs.All(pack => pack.Id != catalog.ActivePackId))
        {
            catalog.ActivePackId = catalog.Packs[0].Id;
        }
    }

    private static void ApplyActiveFlag(ModPackCatalog catalog)
    {
        foreach (var pack in catalog.Packs)
        {
            pack.IsActive = pack.Id == catalog.ActivePackId;
        }
    }

    private static ModPackInfo CreatePack(string name)
    {
        return new ModPackInfo
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
    }

    private static string NextPackName(ModPackCatalog catalog, string prefix)
    {
        var index = 1;
        while (catalog.Packs.Any(pack => pack.Name.Equals($"{prefix} {index}", StringComparison.OrdinalIgnoreCase)))
        {
            index++;
        }

        return $"{prefix} {index}";
    }

    private static string UniquePackName(ModPackCatalog catalog, string name, string? excludedPackId = null)
    {
        if (catalog.Packs.All(pack =>
                pack.Id == excludedPackId
                || !pack.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return name;
        }

        var index = 2;
        while (catalog.Packs.Any(pack =>
                   pack.Id != excludedPackId
                   && pack.Name.Equals($"{name} {index}", StringComparison.OrdinalIgnoreCase)))
        {
            index++;
        }

        return $"{name} {index}";
    }

    private static string NormalizePackName(string name)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException("模组包名称不能为空。");
        }

        return trimmed;
    }

    private static ModPackInfo RequirePack(ModPackCatalog catalog, string packId)
    {
        return catalog.Packs.FirstOrDefault(pack => pack.Id == packId)
            ?? throw new InvalidOperationException("找不到指定模组包。");
    }

    private static bool IsActive(ModPackCatalog catalog, ModPackInfo pack)
    {
        return pack.Id == catalog.ActivePackId;
    }

    private string GetGameModsRoot()
    {
        var game = state.GameDirectory ?? throw new InvalidOperationException("尚未连接游戏目录。");
        return Path.Combine(game.Path, "Mods");
    }

    private static string GetPackRoot(ModPackInfo pack) => Path.Combine(AppPaths.ModPacks, pack.Id);

    private static string GetPackModsRoot(ModPackInfo pack) => Path.Combine(GetPackRoot(pack), "mods");

    private static string GetPackManifestPath(ModPackInfo pack) => Path.Combine(GetPackRoot(pack), "pack.json");

    private void EnsureGameNotRunning()
    {
        runLock.Refresh();
        if (state.IsGameRunning)
        {
            throw new InvalidOperationException("游戏正在运行，不能切换或导入模组包。");
        }
    }

    private static void NormalizeEntry(ModPackEntry entry)
    {
        entry.FolderName = FileSystemHelper.SafeFilePart(string.IsNullOrWhiteSpace(entry.FolderName)
            ? entry.Name
            : entry.FolderName);
        if (string.IsNullOrWhiteSpace(entry.UniqueId))
        {
            entry.UniqueId = $"folder:{entry.FolderName}";
        }

        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            entry.Name = entry.FolderName;
        }

        if (string.IsNullOrWhiteSpace(entry.Version))
        {
            entry.Version = "-";
        }
    }

    private static ModPackInfo CreatePortablePack(ModPackInfo pack)
    {
        return new ModPackInfo
        {
            Id = pack.Id,
            Name = pack.Name,
            CreatedAt = pack.CreatedAt,
            UpdatedAt = DateTime.Now,
            Entries = pack.Entries.Select(entry => new ModPackEntry
            {
                UniqueId = entry.UniqueId,
                Name = entry.Name,
                Version = entry.Version,
                NexusModId = entry.NexusModId,
                FolderName = entry.FolderName,
                IsMissing = false
            }).ToList()
        };
    }

    private static void ValidateArchivePaths(ZipArchive archive)
    {
        const string root = @"C:\LSMA\ModPack\";
        foreach (var entry in archive.Entries)
        {
            var resolved = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("模组包包含不安全路径。");
            }
        }
    }

    private static void AddDirectoryToArchive(ZipArchive archive, string sourceDirectory, string archiveRoot)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            archive.CreateEntryFromFile(file, $"{archiveRoot}/{relative}", CompressionLevel.Fastest);
        }
    }

    private static string? FirstArchiveFolder(string path)
    {
        var normalized = path.Replace('\\', '/');
        var index = normalized.IndexOf('/', StringComparison.Ordinal);
        return index <= 0 ? null : normalized[..index];
    }

    private static string CombineArchivePath(string root, string relativePath)
    {
        var cleanRoot = root.Trim('/');
        var cleanRelative = relativePath.TrimStart('/');
        return string.IsNullOrWhiteSpace(cleanRoot)
            ? cleanRelative
            : $"{cleanRoot}/{cleanRelative}";
    }

    private static async Task RollbackSwitchAsync(
        List<(string Source, string Destination)> targetMoved,
        List<(string Source, string Destination)> currentMoved,
        string gameMods,
        string targetStorage,
        string currentStorage)
    {
        foreach (var move in targetMoved.AsEnumerable().Reverse())
        {
            if (Directory.Exists(move.Destination) && !Directory.Exists(move.Source))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(move.Source)!);
                Directory.Move(move.Destination, move.Source);
            }
        }

        foreach (var move in currentMoved.AsEnumerable().Reverse())
        {
            if (Directory.Exists(move.Destination) && !Directory.Exists(move.Source))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(move.Source)!);
                Directory.Move(move.Destination, move.Source);
            }
        }

        await Task.CompletedTask;
    }

    private sealed record ImportedModPackPackage(ModPackInfo Pack, string ArchiveRoot);

    private sealed record PackagedEntry(string ArchiveRoot, ModPackEntry Entry);
}
