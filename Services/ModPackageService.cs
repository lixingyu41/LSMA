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
    ExternalArchiveReader externalArchiveReader,
    FileSystemSafeService files,
    LoggingService logging)
{
    public async Task<ModInstallPlan> InspectAsync(string packagePath)
    {
        var plan = new ModInstallPlan { PackagePath = packagePath };
        if (!File.Exists(packagePath))
        {
            plan.Blockers.Add("找不到所选压缩包。");
            return plan;
        }

        var preparedPackagePath = packagePath;
        if (!Path.GetExtension(packagePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                preparedPackagePath = await externalArchiveReader.ConvertToZipAsync(packagePath);
            }
            catch (Exception exception)
            {
                plan.Blockers.Add(exception.Message);
                return plan;
            }
        }

        plan.PreparedPackagePath = preparedPackagePath;
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
                    plan.Blockers.Add("压缩包中未找到模组信息文件。");
                    return;
                }
                if (manifests.Count > 1 && manifests.Any(entry => entry.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)))
                {
                    plan.Blockers.Add("压缩包根目录与子目录同时包含多个模组，无法安全确定安装范围。");
                    return;
                }

                foreach (var entry in manifests)
                {
                    await using var stream = entry.Open();
                    var manifest = await JsonSerializer.DeserializeAsync<ModManifest>(stream, JsonHelper.Options);
                    if (manifest is null || string.IsNullOrWhiteSpace(manifest.UniqueID))
                    {
                        plan.Blockers.Add("压缩包包含无效的模组信息文件。");
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
                        ArchiveRoot = archiveRoot,
                        DestinationFolderName = destinationName,
                        Manifest = manifest,
                        ExistingMod = existing,
                        PreserveConfiguration = existing is not null
                            && File.Exists(Path.Combine(existing.FolderPath, "config.json"))
                    };

                    if (archiveRoot.Count(character => character == '/') >= 1)
                    {
                        item.Warnings.Add("压缩包存在多层目录，安装时会自动整理。");
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

                var availableIds = state.Mods
                    .Where(mod => mod.IsEnabled && mod.Manifest?.UniqueID is not null)
                    .Select(mod => mod.Manifest!.UniqueID!)
                    .Concat(plan.Items.Select(item => item.Manifest.UniqueID!))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var versions = state.Mods.Where(mod => mod.IsEnabled && mod.Manifest?.UniqueID is not null)
                    .GroupBy(mod => mod.Manifest!.UniqueID!, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First().Manifest!.Version, StringComparer.OrdinalIgnoreCase);
                foreach (var packaged in plan.Items)
                {
                    versions[packaged.Manifest.UniqueID!] = packaged.Manifest.Version;
                }
                foreach (var item in plan.Items)
                {
                    foreach (var dependency in (item.Manifest.Dependencies ?? []).Where(dependency => dependency.IsRequired))
                    {
                        if (dependency.UniqueID is not null && !availableIds.Contains(dependency.UniqueID))
                        {
                            item.Blockers.Add($"缺少前置模组：{dependency.UniqueID}");
                        }
                        else if (dependency.UniqueID is not null
                            && versions.TryGetValue(dependency.UniqueID, out var version)
                            && !VersionHelper.IsAtLeast(version, dependency.MinimumVersion))
                        {
                            item.Blockers.Add($"前置版本不足：{dependency.UniqueID} 需要 {dependency.MinimumVersion}");
                        }
                    }

                    if (item.Manifest.ContentPackFor?.UniqueID is { Length: > 0 } parentId
                        && !availableIds.Contains(parentId))
                    {
                        item.Blockers.Add($"内容包需要主模组：{parentId}");
                    }

                    if (!string.IsNullOrWhiteSpace(item.Manifest.MinimumApiVersion))
                    {
                        item.Warnings.Add($"需要 SMAPI {item.Manifest.MinimumApiVersion} 或更高版本，请在启动前检查确认。");
                    }
                }
            });
        }
        catch (Exception exception)
        {
            plan.Blockers.Add("无法读取压缩包，请确认文件完整。");
            await logging.ErrorAsync("预检模组压缩包失败", exception);
        }

        foreach (var item in plan.Items)
        {
            plan.Blockers.AddRange(item.Blockers.Select(message => $"{item.Name}：{message}"));
            plan.Warnings.AddRange(item.Warnings.Select(message => $"{item.Name}：{message}"));
        }

        return plan;
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
                await ExtractItemAsync(string.IsNullOrWhiteSpace(plan.PreparedPackagePath) ? plan.PackagePath : plan.PreparedPackagePath, item, stagedFolder);
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
        if (!string.IsNullOrWhiteSpace(plan.PreparedPackagePath)
            && !plan.PreparedPackagePath.Equals(plan.PackagePath, StringComparison.OrdinalIgnoreCase)
            && File.Exists(plan.PreparedPackagePath))
        {
            files.EnsureInside(plan.PreparedPackagePath, AppPaths.Temp);
            File.Delete(plan.PreparedPackagePath);
        }

        return Task.CompletedTask;
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
