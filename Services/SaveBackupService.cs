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
            var folder = FileSystemHelper.SafeFilePart(save.FolderName);
            var farm = FileSystemHelper.SafeFilePart(save.FarmName);
            var farmer = FileSystemHelper.SafeFilePart(save.FarmerName);
            var path = Path.Combine(AppPaths.SaveBackups, $"{folder}_{farm}_{farmer}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            await files.CreateVerifiedZipAsync(save.FolderPath, path);
            Trim();
            await logging.InfoAsync($"存档备份成功：{save.FarmerName} ({operation})");
            return new BackupRecord
            {
                ItemName = save.FarmerName,
                ZipPath = path,
                CreatedAt = DateTime.Now,
                Operation = operation,
                Succeeded = true
            };
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync($"创建存档备份失败：{save.FarmerName}", exception);
            throw;
        }
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
