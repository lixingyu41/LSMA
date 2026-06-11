using System.IO.Compression;
using System.Text.Json;
using LSMA.Models;
using LSMA.Utilities;

namespace LSMA.Services;

public sealed class ModPackageService(
    AppStateService state,
    GameRunLockService runLock,
    ModScannerService scanner,
    ModBackupService backups,
    SettingsService settings,
    ExternalArchiveReader externalArchiveReader,
    FileSystemSafeService files,
    LoggingService logging)
{
    private static readonly IReadOnlyDictionary<string, long> KnownDependencyNexusIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
    {
        ["Pathoschild.ContentPatcher"] = 1915,
        ["Pathoschild.SMAPI"] = 2400,
        ["spacechase0.SpaceCore"] = 1348,
        ["spacechase0.JsonAssets"] = 1720,
        ["spacechase0.GenericModConfigMenu"] = 5098,
        ["Cherry.ExpandedPreconditionsUtility"] = 6529,
        ["aedenthorn.ExtraMapLayers"] = 9633,
        ["ZeroMeters.SAAT.Mod"] = 10747,
        ["Cherry.ShopTileFramework"] = 5005,
        ["PeacefulEnd.ContentPatcherAnimations"] = 3853,
        ["Esca.FarmTypeManager"] = 3231
    };

    public Task<ModInstallPlan> InspectAsync(string packagePath) => InspectAsync([packagePath]);

    public async Task<ModInstallPlan> InspectAsync(IEnumerable<string> packagePaths)
    {
        var paths = packagePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var plan = new ModInstallPlan { PackagePath = paths.Count == 1 ? paths[0] : "批量安装" };
        if (paths.Count == 0)
        {
            plan.Blockers.Add("没有收到可安装的文件或文件夹。");
            return plan;
        }

        foreach (var packagePath in paths)
        {
            string preparedPackagePath;
            try
            {
                preparedPackagePath = await PreparePackageSourceAsync(packagePath);
            }
            catch (Exception exception)
            {
                plan.Blockers.Add($"{DisplaySourceName(packagePath)}：{exception.Message}");
                await logging.ErrorAsync($"预处理模组来源失败：{packagePath}", exception);
                continue;
            }

            if (paths.Count == 1)
            {
                plan.PreparedPackagePath = preparedPackagePath;
            }

            await InspectPreparedPackageAsync(plan, packagePath, preparedPackagePath);
        }

        AddBatchConflicts(plan);
        AddDependencyWarnings(plan);
        AddPlanMessages(plan);
        return plan;
    }

    private async Task<string> PreparePackageSourceAsync(string packagePath)
    {
        if (Directory.Exists(packagePath))
        {
            return await CreateZipFromDirectoryAsync(packagePath);
        }

        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException("找不到所选文件或文件夹。", packagePath);
        }

        return Path.GetExtension(packagePath).Equals(".zip", StringComparison.OrdinalIgnoreCase)
            ? packagePath
            : await externalArchiveReader.ConvertToZipAsync(packagePath);
    }

    private async Task<string> CreateZipFromDirectoryAsync(string directoryPath)
    {
        var preparedPackagePath = Path.Combine(AppPaths.Temp, $"folder_package_{Guid.NewGuid():N}.zip");
        Directory.CreateDirectory(AppPaths.Temp);
        try
        {
            await Task.Run(() =>
            {
                if (!Directory.EnumerateFileSystemEntries(directoryPath).Any())
                {
                    throw new InvalidDataException("文件夹为空。");
                }

                ZipFile.CreateFromDirectory(
                    directoryPath,
                    preparedPackagePath,
                    CompressionLevel.Fastest,
                    includeBaseDirectory: true);
            });
            await logging.InfoAsync($"已读取拖入的模组文件夹：{directoryPath}");
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

    private async Task InspectPreparedPackageAsync(ModInstallPlan plan, string packagePath, string preparedPackagePath)
    {
        try
        {
            await Task.Run(async () =>
            {
                using var archive = ZipFile.OpenRead(preparedPackagePath);
                ValidateArchivePaths(archive);
                var manifests = archive.Entries
                    .Where(entry => entry.FullName.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (manifests.Count == 0)
                {
                    plan.Blockers.Add($"{DisplaySourceName(packagePath)}：压缩包或文件夹中未找到模组信息文件。");
                    return;
                }

                if (manifests.Count > 1 && manifests.Any(entry => entry.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)))
                {
                    plan.Blockers.Add($"{DisplaySourceName(packagePath)}：根目录与子目录同时包含多个模组，无法安全确定安装范围。");
                    return;
                }

                foreach (var entry in manifests)
                {
                    await using var stream = entry.Open();
                    var manifest = await JsonSerializer.DeserializeAsync<ModManifest>(stream, JsonHelper.Options);
                    if (manifest is null || string.IsNullOrWhiteSpace(manifest.UniqueID))
                    {
                        plan.Blockers.Add($"{DisplaySourceName(packagePath)}：包含无效的模组信息文件。");
                        continue;
                    }

                    var archiveRoot = Path.GetDirectoryName(entry.FullName.Replace('/', Path.DirectorySeparatorChar))?
                        .Replace(Path.DirectorySeparatorChar, '/') ?? string.Empty;
                    var existing = state.Mods.FirstOrDefault(mod => !mod.IsArchived
                        && mod.Manifest?.UniqueID?.Equals(manifest.UniqueID, StringComparison.OrdinalIgnoreCase) == true);
                    var destinationName = existing?.FolderName
                        ?? FileSystemHelper.SafeFilePart(manifest.Name ?? manifest.UniqueID);
                    var folderConflict = state.Mods.FirstOrDefault(mod => !mod.IsArchived
                        && mod.FolderName.Equals(destinationName, StringComparison.OrdinalIgnoreCase)
                        && mod.Manifest?.UniqueID?.Equals(manifest.UniqueID, StringComparison.OrdinalIgnoreCase) != true);
                    var item = new ModInstallPlanItem
                    {
                        PackagePath = packagePath,
                        PreparedPackagePath = preparedPackagePath,
                        ArchiveRoot = archiveRoot,
                        DestinationFolderName = destinationName,
                        Manifest = manifest,
                        ExistingMod = existing,
                        PreserveConfiguration = existing is not null
                            && File.Exists(Path.Combine(existing.FolderPath, "config.json"))
                    };

                    if (archiveRoot.Count(character => character == '/') >= 1)
                    {
                        item.Warnings.Add("压缩包或文件夹存在多层目录，安装时会自动整理。");
                    }

                    if (!string.IsNullOrWhiteSpace(manifest.EntryDll)
                        && !ContainsEntry(archive, archiveRoot, manifest.EntryDll))
                    {
                        item.Blockers.Add("模组程序文件缺失。");
                    }

                    if (folderConflict is not null)
                    {
                        item.Blockers.Add($"目标文件夹已被“{folderConflict.Name}”使用，不能安全覆盖。");
                    }

                    plan.Items.Add(item);
                }
            });
        }
        catch (Exception exception)
        {
            plan.Blockers.Add($"{DisplaySourceName(packagePath)}：无法读取压缩包或文件夹，请确认内容完整。");
            await logging.ErrorAsync($"预检模组来源失败：{packagePath}", exception);
        }
    }

    private void AddBatchConflicts(ModInstallPlan plan)
    {
        foreach (var group in plan.Items
                     .GroupBy(item => item.DestinationFolderName, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            plan.Blockers.Add($"批量安装中包含重复目标文件夹：{group.Key}。");
        }

        foreach (var group in plan.Items
                     .Where(item => !string.IsNullOrWhiteSpace(item.Manifest.UniqueID))
                     .GroupBy(item => item.Manifest.UniqueID!, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            plan.Blockers.Add($"批量安装中包含重复模组：{group.Key}。");
        }
    }

    private void AddDependencyWarnings(ModInstallPlan plan)
    {
        var installedModsById = state.Mods
            .Where(mod => !mod.IsArchived && mod.Manifest?.UniqueID is not null)
            .GroupBy(mod => mod.Manifest!.UniqueID!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var availableIds = installedModsById.Values
            .Where(mod => mod.IsEnabled)
            .Select(mod => mod.Manifest!.UniqueID!)
            .Concat(plan.Items.Select(item => item.Manifest.UniqueID!).Where(id => !string.IsNullOrWhiteSpace(id)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var versions = installedModsById.Values.Where(mod => mod.IsEnabled)
            .GroupBy(mod => mod.Manifest!.UniqueID!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Manifest!.Version, StringComparer.OrdinalIgnoreCase);
        foreach (var packaged in plan.Items.Where(item => !string.IsNullOrWhiteSpace(item.Manifest.UniqueID)))
        {
            versions[packaged.Manifest.UniqueID!] = packaged.Manifest.Version;
        }

        foreach (var item in plan.Items)
        {
            foreach (var dependency in (item.Manifest.Dependencies ?? []).Where(dependency => dependency.IsRequired))
            {
                if (dependency.UniqueID is not null && !availableIds.Contains(dependency.UniqueID))
                {
                    item.MissingDependencies.Add(CreateMissingDependency(
                        item,
                        dependency.UniqueID,
                        dependency.MinimumVersion,
                        installedModsById,
                        $"缺少前置模组：{dependency.UniqueID}"));
                }
                else if (dependency.UniqueID is not null
                    && versions.TryGetValue(dependency.UniqueID, out var version)
                    && !VersionHelper.IsAtLeast(version, dependency.MinimumVersion))
                {
                    item.MissingDependencies.Add(CreateMissingDependency(
                        item,
                        dependency.UniqueID,
                        dependency.MinimumVersion,
                        installedModsById,
                        $"前置版本不足：{dependency.UniqueID} 需要 {dependency.MinimumVersion}",
                        version));
                }
            }

            if (item.Manifest.ContentPackFor?.UniqueID is { Length: > 0 } parentId
                && !availableIds.Contains(parentId))
            {
                item.MissingDependencies.Add(CreateMissingDependency(
                    item,
                    parentId,
                    item.Manifest.ContentPackFor.MinimumVersion,
                    installedModsById,
                    $"内容包需要主模组：{parentId}"));
            }

            if (!string.IsNullOrWhiteSpace(item.Manifest.MinimumApiVersion))
            {
                item.Warnings.Add($"需要 SMAPI {item.Manifest.MinimumApiVersion} 或更高版本，请在启动前检查确认。");
            }
        }
    }

    private static void AddPlanMessages(ModInstallPlan plan)
    {
        foreach (var item in plan.Items)
        {
            plan.Blockers.AddRange(item.Blockers.Select(message => $"{item.Name}：{message}"));
            plan.Warnings.AddRange(item.Warnings.Select(message => $"{item.Name}：{message}"));
            foreach (var missing in item.MissingDependencies)
            {
                if (!plan.MissingDependencies.Any(value => value.UniqueId.Equals(missing.UniqueId, StringComparison.OrdinalIgnoreCase)))
                {
                    plan.MissingDependencies.Add(missing);
                }

                plan.Warnings.Add($"{item.Name}：{missing.DetailText}，可以先安装当前模组，补齐前置前游戏可能无法启动。");
            }
        }
    }

    public async Task<ModInstallResult> InstallAsync(ModInstallPlan plan)
    {
        if (!plan.CanInstall)
        {
            throw new InvalidOperationException("安装计划包含阻断问题，不能执行。");
        }

        runLock.Refresh();
        if (state.IsGameRunning)
        {
            throw new InvalidOperationException("游戏正在运行，不能安装模组。");
        }

        var game = state.GameDirectory ?? throw new InvalidOperationException("尚未连接游戏目录。");
        var result = new ModInstallResult();
        foreach (var item in plan.Items)
        {
            var workRoot = Path.Combine(AppPaths.Temp, $"install_{Guid.NewGuid():N}");
            var stagedFolder = Path.Combine(workRoot, item.DestinationFolderName);
            string? holdFolder = null;
            string? targetFolder = null;
            string? backupPath = null;
            var rolledBack = false;
            var targetChanged = false;
            await AppendTransactionAsync(item, null, null, false, false, null, "Started");
            try
            {
                await ExtractItemAsync(ResolvePreparedPackagePath(plan, item), item, stagedFolder);
                var targetRoot = item.ExistingMod is null
                    ? Path.Combine(game.Path, "Mods")
                    : Path.GetDirectoryName(item.ExistingMod.FolderPath)!;
                targetFolder = Path.Combine(targetRoot, item.DestinationFolderName);
                if (item.ExistingMod is not null)
                {
                    backupPath = (await backups.CreateAsync(item.ExistingMod, "覆盖安装")).ZipPath;
                    if (item.PreserveConfiguration)
                    {
                        File.Copy(Path.Combine(item.ExistingMod.FolderPath, "config.json"), Path.Combine(stagedFolder, "config.json"), true);
                    }

                    holdFolder = Path.Combine(AppPaths.Temp, $"old_{Guid.NewGuid():N}");
                    await files.MoveDirectoryAsync(item.ExistingMod.FolderPath, holdFolder, AppPaths.Temp);
                    targetChanged = true;
                }
                else
                {
                    await WriteNewInstallRestorePointAsync(item, targetFolder);
                }

                await files.MoveDirectoryAsync(stagedFolder, targetFolder, targetRoot);
                targetChanged = true;
                var verified = await scanner.ScanDirectoryAsync(targetFolder, targetRoot.EndsWith("Mods", StringComparison.OrdinalIgnoreCase));
                if (verified.Manifest?.UniqueID?.Equals(item.Manifest.UniqueID, StringComparison.OrdinalIgnoreCase) != true)
                {
                    throw new InvalidDataException("安装后的模组未通过识别验证。");
                }

                if (holdFolder is not null)
                {
                    await files.DeleteDirectoryAsync(holdFolder, AppPaths.Temp);
                }

                result.InstalledCount++;
                result.Messages.Add($"{item.Name} 已安装。");
                await logging.InfoAsync($"事务安装模组成功：{item.Name}");
                await AppendTransactionAsync(item, targetFolder, backupPath, true, false, null, "Succeeded");
            }
            catch (Exception exception)
            {
                result.FailedCount++;
                string? rollbackError = null;
                try
                {
                    if (targetFolder is not null && Directory.Exists(targetFolder))
                    {
                        await files.QuarantineDirectoryAsync(targetFolder, item.Name);
                    }

                    if (holdFolder is not null && Directory.Exists(holdFolder) && item.ExistingMod is not null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(item.ExistingMod.FolderPath)!);
                        await files.MoveDirectoryAsync(
                            holdFolder,
                            item.ExistingMod.FolderPath,
                            Path.GetDirectoryName(item.ExistingMod.FolderPath)!);
                        rolledBack = true;
                    }
                    else if (item.ExistingMod is null && targetChanged)
                    {
                        rolledBack = true;
                    }
                }
                catch (Exception rollbackException)
                {
                    rollbackError = rollbackException.Message;
                    await logging.ErrorAsync($"事务安装模组回滚失败：{item.Name}", rollbackException);
                }

                await logging.ErrorAsync($"事务安装模组失败：{item.Name}", exception);
                var failure = rollbackError is null
                    ? exception.Message
                    : $"{exception.Message}；回滚失败：{rollbackError}";
                result.Messages.Add($"{item.Name} 安装失败：{failure}。已尝试恢复。");
                await AppendTransactionAsync(
                    item,
                    targetFolder,
                    backupPath,
                    false,
                    rolledBack,
                    failure,
                    rolledBack ? "RolledBack" : "Failed");
            }
            finally
            {
                if (Directory.Exists(workRoot))
                {
                    await files.DeleteDirectoryAsync(workRoot, AppPaths.Temp);
                }
            }
        }

        await CleanupPreparedPackageAsync(plan);
        return result;
    }

    public Task CleanupPreparedPackageAsync(ModInstallPlan plan)
    {
        var originalPaths = plan.Items
            .Select(item => item.PackagePath)
            .Append(plan.PackagePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var preparedPaths = plan.Items
            .Select(item => item.PreparedPackagePath)
            .Append(plan.PreparedPackagePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var preparedPath in preparedPaths)
        {
            if (!originalPaths.Contains(preparedPath) && File.Exists(preparedPath))
            {
                files.EnsureInside(preparedPath, AppPaths.Temp);
                File.Delete(preparedPath);
            }
        }

        return Task.CompletedTask;
    }

    private static string ResolvePreparedPackagePath(ModInstallPlan plan, ModInstallPlanItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.PreparedPackagePath))
        {
            return item.PreparedPackagePath;
        }

        return string.IsNullOrWhiteSpace(plan.PreparedPackagePath) ? plan.PackagePath : plan.PreparedPackagePath;
    }

    private static string DisplaySourceName(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private static void ValidateArchivePaths(ZipArchive archive)
    {
        const string root = @"C:\LSMA\Archive\";
        foreach (var entry in archive.Entries)
        {
            var resolved = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("压缩包包含不安全路径。");
            }
        }
    }

    private static bool ContainsEntry(ZipArchive archive, string root, string relativePath)
    {
        var expected = string.IsNullOrEmpty(root)
            ? relativePath.Replace('\\', '/')
            : $"{root.TrimEnd('/')}/{relativePath.Replace('\\', '/')}";
        return archive.Entries.Any(entry => entry.FullName.Equals(expected, StringComparison.OrdinalIgnoreCase));
    }

    private MissingDependencyAction CreateMissingDependency(
        ModInstallPlanItem item,
        string uniqueId,
        string? minimumVersion,
        IReadOnlyDictionary<string, ModInfo> installedModsById,
        string message,
        string? installedVersion = null)
    {
        return new MissingDependencyAction
        {
            UniqueId = uniqueId,
            SourceModName = item.Name,
            MinimumVersion = minimumVersion,
            InstalledVersion = installedVersion,
            NexusModId = ResolveNexusModId(uniqueId, installedModsById),
            Message = message
        };
    }

    private long? ResolveNexusModId(string uniqueId, IReadOnlyDictionary<string, ModInfo> installedModsById)
    {
        if (installedModsById.TryGetValue(uniqueId, out var mod) && mod.NexusModId is { } installedId)
        {
            return installedId;
        }

        if (settings.Current.NexusBindings.TryGetValue(uniqueId, out var binding))
        {
            return binding;
        }

        return KnownDependencyNexusIds.TryGetValue(uniqueId, out var knownId)
            ? knownId
            : null;
    }

    private static async Task ExtractItemAsync(string packagePath, ModInstallPlanItem item, string destination)
    {
        Directory.CreateDirectory(destination);
        using var archive = ZipFile.OpenRead(packagePath);
        var prefix = string.IsNullOrWhiteSpace(item.ArchiveRoot) ? string.Empty : item.ArchiveRoot.TrimEnd('/') + "/";
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
            var permitted = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(permitted, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("压缩包包含不安全路径。");
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

    private static Task WriteNewInstallRestorePointAsync(ModInstallPlanItem item, string targetFolder)
    {
        var recordPath = Path.Combine(AppPaths.FailedStates, "new-install-restore-points.jsonl");
        Directory.CreateDirectory(AppPaths.FailedStates);
        var record = JsonSerializer.Serialize(new
        {
            Operation = "新安装",
            item.Name,
            item.Manifest.UniqueID,
            Target = targetFolder,
            Rollback = "移除新安装目录",
            CreatedAt = DateTime.Now
        }, JsonHelper.Options).Replace(Environment.NewLine, string.Empty);
        return File.AppendAllTextAsync(recordPath, record + Environment.NewLine);
    }

    private static Task AppendTransactionAsync(
        ModInstallPlanItem item,
        string? target,
        string? backup,
        bool success,
        bool rolledBack,
        string? error,
        string status)
    {
        var path = Path.Combine(AppPaths.FailedStates, "mod-install-transactions.jsonl");
        Directory.CreateDirectory(AppPaths.FailedStates);
        var value = JsonSerializer.Serialize(new
        {
            Operation = item.ExistingMod is null ? "安装" : "更新",
            item.Name,
            item.Manifest.UniqueID,
            Target = target,
            Backup = backup,
            Status = status,
            Success = success,
            RolledBack = rolledBack,
            Error = error,
            CreatedAt = DateTime.Now
        }, JsonHelper.Options).Replace(Environment.NewLine, string.Empty);
        return File.AppendAllTextAsync(path, value + Environment.NewLine);
    }
}
