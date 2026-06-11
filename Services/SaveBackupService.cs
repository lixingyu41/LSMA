using System.IO.Compression;
using System.Text.Json;
using LSMA.Models;
using LSMA.Utilities;

namespace LSMA.Services;

public sealed class SaveBackupService(
    AppStateService state,
    GameRunLockService runLock,
    SettingsService settings,
    FileSystemSafeService files,
    LoggingService logging)
{
    public async Task<BackupRecord> CreateAsync(SaveInfo save, string operation = "手动备份")
    {
        try
        {
            var record = await CreateBackupForFolderAsync(
                save.FolderPath,
                save.FolderName,
                $"{save.FarmName}_{save.FarmerName}",
                save.FarmerName,
                operation,
                trimBackups: true);
            await logging.InfoAsync($"存档备份成功：{save.FarmerName} ({operation})");
            return record;
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync($"创建存档备份失败：{save.FarmerName}", exception);
            throw;
        }
    }

    public async Task ExportAsync(SaveInfo save, string destinationPath)
    {
        try
        {
            await files.CreateVerifiedZipAsync(save.FolderPath, destinationPath, includeBaseDirectory: true);
            await logging.InfoAsync($"已导出存档：{save.FarmerName} -> {destinationPath}");
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync($"导出存档失败：{save.FarmerName}", exception);
            throw;
        }
    }

    public async Task<SaveImportResult> ImportAsync(string archivePath)
    {
        runLock.Refresh();
        if (state.IsGameRunning)
        {
            throw new InvalidOperationException("游戏正在运行，不能导入存档。");
        }

        var preparedArchivePath = await PrepareSaveArchiveAsync(archivePath);
        if (!string.Equals(Path.GetExtension(preparedArchivePath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("当前只支持导入 .zip 存档压缩包。");
        }

        var extraction = Path.Combine(AppPaths.Temp, $"save_import_extract_{Guid.NewGuid():N}");
        var staging = Path.Combine(AppPaths.Temp, $"save_import_stage_{Guid.NewGuid():N}");
        Directory.CreateDirectory(extraction);

        var folderName = string.Empty;
        string? safetyBackup = null;
        var success = false;
        var rolledBack = false;
        string? error = null;
        var replacedExisting = false;
        await AppendImportRecordAsync(archivePath, null, null, false, false, null, "Started");

        try
        {
            await Task.Run(() => ExtractZipArchive(preparedArchivePath, extraction));
            var candidate = FindSingleSaveDirectory(extraction);
            folderName = candidate.FolderName;
            CopyDirectory(candidate.DirectoryPath, staging);
            VerifySaveDirectory(staging, folderName);

            Directory.CreateDirectory(AppPaths.SaveSource);
            var destination = Path.Combine(AppPaths.SaveSource, folderName);
            files.EnsureInside(destination, AppPaths.SaveSource);
            replacedExisting = Directory.Exists(destination);
            if (replacedExisting)
            {
                safetyBackup = (await CreateBackupForFolderAsync(
                    destination,
                    folderName,
                    folderName,
                    folderName,
                    "导入前自动备份",
                    trimBackups: true)).ZipPath;
            }

            var displaced = Path.Combine(
                AppPaths.FailedStates,
                $"{FileSystemHelper.SafeFilePart(folderName)}_import_displaced_{DateTime.Now:yyyyMMdd_HHmmss_fff}");
            try
            {
                state.HasPendingRecovery = true;
                if (replacedExisting)
                {
                    Directory.CreateDirectory(AppPaths.FailedStates);
                    Directory.Move(destination, displaced);
                }

                await files.MoveDirectoryAsync(staging, destination, AppPaths.SaveSource);
                VerifySaveDirectory(destination, folderName);
                if (Directory.Exists(displaced))
                {
                    Directory.Delete(displaced, true);
                }

                state.HasPendingRecovery = false;
                success = true;
                await logging.InfoAsync($"已导入存档：{folderName}");
                return new SaveImportResult
                {
                    FolderName = folderName,
                    ReplacedExisting = replacedExisting,
                    SafetyBackupPath = safetyBackup
                };
            }
            catch
            {
                if (Directory.Exists(destination))
                {
                    Directory.Delete(destination, true);
                }

                if (Directory.Exists(displaced))
                {
                    Directory.Move(displaced, destination);
                    rolledBack = true;
                }
                else if (safetyBackup is not null)
                {
                    await files.RestoreDirectoryFromZipAsync(safetyBackup, destination, AppPaths.SaveSource);
                    rolledBack = true;
                }

                state.HasPendingRecovery = false;
                throw;
            }
        }
        catch (Exception exception)
        {
            error = exception.Message;
            await logging.ErrorAsync("导入存档失败", exception);
            throw;
        }
        finally
        {
            if (Directory.Exists(extraction))
            {
                Directory.Delete(extraction, true);
            }

            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }

            if (!preparedArchivePath.Equals(archivePath, StringComparison.OrdinalIgnoreCase)
                && File.Exists(preparedArchivePath))
            {
                files.EnsureInside(preparedArchivePath, AppPaths.Temp);
                File.Delete(preparedArchivePath);
            }

            await AppendImportRecordAsync(
                archivePath,
                string.IsNullOrWhiteSpace(folderName) ? null : folderName,
                safetyBackup,
                success,
                rolledBack,
                error,
                success ? "Succeeded" : rolledBack ? "RolledBack" : "Failed");
        }
    }

    private async Task<string> PrepareSaveArchiveAsync(string sourcePath)
    {
        if (Directory.Exists(sourcePath))
        {
            var preparedArchivePath = Path.Combine(AppPaths.Temp, $"save_folder_{Guid.NewGuid():N}.zip");
            Directory.CreateDirectory(AppPaths.Temp);
            try
            {
                await Task.Run(() =>
                {
                    if (!Directory.EnumerateFileSystemEntries(sourcePath).Any())
                    {
                        throw new InvalidDataException("存档文件夹为空。");
                    }

                    ZipFile.CreateFromDirectory(
                        sourcePath,
                        preparedArchivePath,
                        CompressionLevel.Fastest,
                        includeBaseDirectory: true);
                });
                await logging.InfoAsync($"已读取拖入的存档文件夹：{sourcePath}");
                return preparedArchivePath;
            }
            catch
            {
                if (File.Exists(preparedArchivePath))
                {
                    File.Delete(preparedArchivePath);
                }

                throw;
            }
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("存档压缩包或文件夹不存在。", sourcePath);
        }

        return sourcePath;
    }

    public async Task<int> CreateAllAsync(IEnumerable<SaveInfo> saves)
    {
        var count = 0;
        foreach (var save in saves.Where(value => value.ParseError is null))
        {
            await CreateAsync(save, "备份全部存档");
            count++;
        }

        return count;
    }

    public IReadOnlyList<SaveBackupEntry> GetBackups(string folderName)
    {
        var prefix = FileSystemHelper.SafeFilePart(folderName) + "_";
        if (!Directory.Exists(AppPaths.SaveBackups))
        {
            return [];
        }

        return Directory.EnumerateFiles(AppPaths.SaveBackups, $"{prefix}*.zip")
            .Select(path => new SaveBackupEntry { Path = path, CreatedAt = File.GetCreationTime(path) })
            .OrderByDescending(entry => entry.CreatedAt)
            .ToList();
    }

    public DateTime? GetLatestBackup(string folderName)
    {
        return GetBackups(folderName).FirstOrDefault()?.CreatedAt;
    }

    public async Task RestoreAsync(SaveInfo save, SaveBackupEntry entry)
    {
        runLock.Refresh();
        if (state.IsGameRunning)
        {
            throw new InvalidOperationException("游戏正在运行，不能恢复存档。");
        }

        string? safetyBackup = null;
        var success = false;
        var rolledBack = false;
        string? error = null;
        await AppendRestoreRecordAsync(save, entry, null, false, false, null, "Started");
        try
        {
            safetyBackup = (await CreateAsync(save, "恢复前自动备份")).ZipPath;
            state.HasPendingRecovery = true;
            await files.RestoreDirectoryFromZipAsync(entry.Path, save.FolderPath, AppPaths.SaveSource);
            if (!Directory.EnumerateFiles(save.FolderPath).Any())
            {
                throw new InvalidDataException("恢复后的存档为空。");
            }

            success = true;
            state.HasPendingRecovery = false;
            await logging.InfoAsync($"恢复存档成功：{save.FarmerName}");
        }
        catch (Exception exception)
        {
            error = exception.Message;
            if (state.HasPendingRecovery)
            {
                try
                {
                    if (safetyBackup is not null)
                    {
                        await files.RestoreDirectoryFromZipAsync(safetyBackup, save.FolderPath, AppPaths.SaveSource);
                        rolledBack = true;
                        state.HasPendingRecovery = false;
                    }
                }
                catch (Exception rollbackException)
                {
                    await logging.ErrorAsync("存档恢复回滚失败", rollbackException);
                }
            }
            else
            {
                state.HasPendingRecovery = false;
            }

            await logging.ErrorAsync("存档恢复失败", exception);
            throw;
        }
        finally
        {
            await AppendRestoreRecordAsync(
                save,
                entry,
                safetyBackup,
                success,
                rolledBack,
                error,
                success ? "Succeeded" : rolledBack ? "RolledBack" : "Failed");
        }
    }

    private static Task AppendRestoreRecordAsync(
        SaveInfo save,
        SaveBackupEntry entry,
        string? safetyBackup,
        bool success,
        bool rolledBack,
        string? error,
        string status)
    {
        var record = JsonSerializer.Serialize(new
        {
            Operation = "恢复存档",
            save.FolderName,
            SourceBackup = entry.Path,
            SafetyBackup = safetyBackup,
            Status = status,
            Success = success,
            RolledBack = rolledBack,
            Error = error,
            CreatedAt = DateTime.Now
        }, JsonHelper.Options).Replace(Environment.NewLine, string.Empty);
        Directory.CreateDirectory(AppPaths.FailedStates);
        return File.AppendAllTextAsync(Path.Combine(AppPaths.FailedStates, "save-transactions.jsonl"), record + Environment.NewLine);
    }

    private async Task<BackupRecord> CreateBackupForFolderAsync(
        string sourceDirectory,
        string folderName,
        string fileNameDetail,
        string itemName,
        string operation,
        bool trimBackups)
    {
        var folder = FileSystemHelper.SafeFilePart(folderName);
        var item = FileSystemHelper.SafeFilePart(fileNameDetail);
        var path = Path.Combine(AppPaths.SaveBackups, $"{folder}_{item}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
        await files.CreateVerifiedZipAsync(sourceDirectory, path);
        if (trimBackups)
        {
            Trim();
        }

        return new BackupRecord
        {
            ItemName = itemName,
            ZipPath = path,
            CreatedAt = DateTime.Now,
            Operation = operation,
            Succeeded = true
        };
    }

    private static Task AppendImportRecordAsync(
        string archivePath,
        string? folderName,
        string? safetyBackup,
        bool success,
        bool rolledBack,
        string? error,
        string status)
    {
        var record = JsonSerializer.Serialize(new
        {
            Operation = "导入存档",
            Archive = archivePath,
            FolderName = folderName,
            SafetyBackup = safetyBackup,
            Status = status,
            Success = success,
            RolledBack = rolledBack,
            Error = error,
            CreatedAt = DateTime.Now
        }, JsonHelper.Options).Replace(Environment.NewLine, string.Empty);
        Directory.CreateDirectory(AppPaths.FailedStates);
        return File.AppendAllTextAsync(Path.Combine(AppPaths.FailedStates, "save-transactions.jsonl"), record + Environment.NewLine);
    }

    private static void ExtractZipArchive(string archivePath, string extraction)
    {
        var extractionRoot = Path.GetFullPath(extraction).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count == 0)
        {
            throw new InvalidDataException("存档压缩包为空。");
        }

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            var relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var targetPath = Path.GetFullPath(Path.Combine(extraction, relativePath));
            if (!targetPath.StartsWith(extractionRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("存档压缩包包含越界文件路径。");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, true);
        }
    }

    private sealed record SaveImportCandidate(string DirectoryPath, string FolderName);

    private static SaveImportCandidate FindSingleSaveDirectory(string extraction)
    {
        var candidates = Directory.EnumerateDirectories(extraction, "*", SearchOption.AllDirectories)
            .Prepend(extraction)
            .Select(TryCreateSaveCandidate)
            .Where(candidate => candidate is not null)
            .Cast<SaveImportCandidate>()
            .ToList();
        if (candidates.Count == 0)
        {
            throw new InvalidDataException("压缩包内未找到 Stardew Valley 存档目录。");
        }

        if (candidates.Count > 1)
        {
            throw new InvalidDataException("压缩包内包含多个存档目录，请一次导入一个存档。");
        }

        return candidates[0];
    }

    private static SaveImportCandidate? TryCreateSaveCandidate(string directory)
    {
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .ToList();
        if (!files.Any(file => file.Name.Equals("SaveGameInfo", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var primary = FindPrimarySaveFile(directory, files);
        return primary is null
            ? null
            : new SaveImportCandidate(directory, primary.Name);
    }

    private static FileInfo? FindPrimarySaveFile(string directory, IReadOnlyList<FileInfo> files)
    {
        var directoryName = Path.GetFileName(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar));
        var candidates = files
            .Where(file => !file.Name.Equals("SaveGameInfo", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(file.Extension))
            .ToList();
        return candidates.FirstOrDefault(file => file.Name.Equals(directoryName, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(file => HasNumericSuffix(file.Name))
            ?? candidates.FirstOrDefault();
    }

    private static bool HasNumericSuffix(string name)
    {
        var separator = name.LastIndexOf('_');
        return separator > 0
            && separator < name.Length - 1
            && name[(separator + 1)..].All(char.IsDigit);
    }

    private static void VerifySaveDirectory(string directory, string folderName)
    {
        if (!File.Exists(Path.Combine(directory, "SaveGameInfo"))
            || !File.Exists(Path.Combine(directory, folderName)))
        {
            throw new InvalidDataException("导入后的目录缺少 Stardew Valley 存档主文件或 SaveGameInfo。");
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, false);
        }
    }

    private void Trim()
    {
        var retention = Math.Max(1, settings.Current.SaveBackupRetention);
        foreach (var path in Directory.EnumerateFiles(AppPaths.SaveBackups, "*.zip")
                     .OrderByDescending(File.GetCreationTimeUtc)
                     .Skip(retention))
        {
            File.Delete(path);
        }
    }
}
