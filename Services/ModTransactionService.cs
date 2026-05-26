using System.Text.Json;
using LSMA.Models;
using LSMA.Utilities;

namespace LSMA.Services;

public sealed class ModTransactionService(
    AppStateService state,
    GameRunLockService runLock,
    ModBackupService backups,
    ModScannerService scanner,
    FileSystemSafeService files,
    LoggingService logging)
{
    public async Task SetEnabledAsync(ModInfo mod, bool enabled)
    {
        var game = state.GameDirectory ?? throw new InvalidOperationException("尚未连接游戏目录。");
        var targetRoot = Path.Combine(game.Path, enabled ? "Mods" : "Mods.Disabled");
        await MoveWithBackupAsync(mod, targetRoot, enabled ? "启用" : "禁用");
    }

    public async Task ArchiveAsync(ModInfo mod)
    {
        var archiveRoot = Path.Combine(AppPaths.ArchivedMods, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        await MoveWithBackupAsync(mod, archiveRoot, "归档");
    }

    public async Task RestoreArchivedAsync(ModInfo mod)
    {
        if (!mod.IsArchived)
        {
            throw new InvalidOperationException("只能恢复已归档的模组。");
        }

        var game = state.GameDirectory ?? throw new InvalidOperationException("尚未连接游戏目录。");
        await MoveWithBackupAsync(mod, Path.Combine(game.Path, "Mods.Disabled"), "恢复归档");
    }

    public async Task RepairNestedDirectoryAsync(ModInfo mod)
    {
        if (mod.SuggestedNestedDirectory is not { } nestedDirectory)
        {
            throw new InvalidOperationException("该模组没有可自动修复的目录问题。");
        }

        var game = state.GameDirectory ?? throw new InvalidOperationException("尚未连接游戏目录。");
        var permittedRoot = Path.Combine(game.Path, mod.IsEnabled ? "Mods" : "Mods.Disabled");
        files.EnsureInside(mod.FolderPath, permittedRoot);
        files.EnsureInside(nestedDirectory, mod.FolderPath);
        EnsureGameNotRunning();

        var record = CreateRecord(mod, "修复目录结构");
        await AppendRecordAsync(record);
        try
        {
            record.BackupPath = (await backups.CreateAsync(mod, "修复目录结构")).ZipPath;
            var nestedEntries = Directory.EnumerateFileSystemEntries(nestedDirectory).ToList();
            if (nestedEntries.Count == 0)
            {
                throw new InvalidDataException("嵌套目录为空，不能自动修复。");
            }

            foreach (var entry in nestedEntries)
            {
                var destination = Path.Combine(mod.FolderPath, Path.GetFileName(entry));
                if (File.Exists(destination) || Directory.Exists(destination))
                {
                    throw new IOException("目录修复可能覆盖已有文件，请手动处理。");
                }
            }

            foreach (var entry in nestedEntries)
            {
                var destination = Path.Combine(mod.FolderPath, Path.GetFileName(entry));
                if (Directory.Exists(entry))
                {
                    Directory.Move(entry, destination);
                }
                else
                {
                    File.Move(entry, destination);
                }
            }

            if (Directory.Exists(nestedDirectory) && !Directory.EnumerateFileSystemEntries(nestedDirectory).Any())
            {
                Directory.Delete(nestedDirectory);
            }

            var verified = await scanner.ScanDirectoryAsync(mod.FolderPath, mod.IsEnabled);
            if (verified.Manifest is null)
            {
                throw new InvalidDataException("目录修复后仍无法读取模组信息文件。");
            }

            record.Success = true;
            record.Status = "Succeeded";
            await logging.InfoAsync($"修复模组目录成功：{mod.Name}");
        }
        catch (Exception exception)
        {
            record.Error = exception.Message;
            try
            {
                if (record.BackupPath is not null)
                {
                    await files.RestoreDirectoryFromZipAsync(record.BackupPath, mod.FolderPath, permittedRoot);
                    record.RolledBack = true;
                }
            }
            catch (Exception rollbackException)
            {
                record.RollbackError = rollbackException.Message;
                await logging.ErrorAsync($"目录修复回滚失败：{mod.Name}", rollbackException);
            }

            await logging.ErrorAsync($"修复模组目录失败：{mod.Name}", exception);
            record.Status = record.RolledBack ? "RolledBack" : "Failed";
            throw;
        }
        finally
        {
            await AppendRecordAsync(record);
        }
    }

    private async Task MoveWithBackupAsync(ModInfo mod, string targetRoot, string operation)
    {
        EnsureGameNotRunning();

        var record = CreateRecord(mod, operation);
        await AppendRecordAsync(record);
        var target = Path.Combine(targetRoot, mod.FolderName);
        var moved = false;
        try
        {
            record.BackupPath = (await backups.CreateAsync(mod, operation)).ZipPath;
            if (Directory.Exists(target))
            {
                throw new IOException("目标位置已存在同名模组目录。");
            }

            await files.MoveDirectoryAsync(mod.FolderPath, target, targetRoot);
            moved = true;
            if (!Directory.Exists(target))
            {
                throw new IOException("操作后未找到目标模组目录。");
            }

            var verified = await scanner.ScanDirectoryAsync(target, operation == "启用");
            if (verified.Manifest is null && mod.Manifest is not null)
            {
                throw new InvalidDataException("操作后的模组无法通过验证。");
            }
            record.Success = true;
            record.Status = "Succeeded";
            await logging.InfoAsync($"{operation}模组成功：{mod.Name}");
        }
        catch (Exception exception)
        {
            record.Error = exception.Message;
            if (moved && Directory.Exists(target) && !Directory.Exists(mod.FolderPath))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(mod.FolderPath)!);
                    Directory.Move(target, mod.FolderPath);
                    record.RolledBack = true;
                }
                catch (Exception rollbackException)
                {
                    record.RollbackError = rollbackException.Message;
                    await logging.ErrorAsync($"模组操作回滚失败：{mod.Name}", rollbackException);
                }
            }

            await logging.ErrorAsync($"{operation}模组失败：{mod.Name}", exception);
            record.Status = record.RolledBack ? "RolledBack" : "Failed";
            throw;
        }
        finally
        {
            await AppendRecordAsync(record);
        }
    }

    private void EnsureGameNotRunning()
    {
        runLock.Refresh();
        if (state.IsGameRunning)
        {
            throw new InvalidOperationException("游戏正在运行，不能修改模组文件。");
        }
    }

    private static ModOperationRecord CreateRecord(ModInfo mod, string operation)
    {
        return new ModOperationRecord
        {
            Mod = mod.Name,
            Operation = operation,
            Source = mod.FolderPath,
            StartedAt = DateTime.Now
        };
    }

    private static async Task AppendRecordAsync(ModOperationRecord record)
    {
        var path = Path.Combine(AppPaths.FailedStates, "mod-transactions.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(record, JsonHelper.Options);
        await File.AppendAllTextAsync(path, json.Replace(Environment.NewLine, string.Empty) + Environment.NewLine);
    }

    private sealed class ModOperationRecord
    {
        public string Mod { get; init; } = string.Empty;
        public string Operation { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
        public string? BackupPath { get; set; }
        public DateTime StartedAt { get; init; }
        public string Status { get; set; } = "Started";
        public bool Success { get; set; }
        public bool RolledBack { get; set; }
        public string? Error { get; set; }
        public string? RollbackError { get; set; }
    }
}
