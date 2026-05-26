using System.IO.Compression;
using System.Text.Json;
using LSMA.Models;
using LSMA.Utilities;

namespace LSMA.Services;

public sealed class LastKnownGoodService(
    AppStateService state,
    GameRunLockService runLock,
    ModScannerService modScanner,
    SaveBackupService saveBackups,
    FileSystemSafeService files,
    LoggingService logging)
{
    public async Task<LastKnownGoodSnapshot?> CaptureIfCleanAsync()
    {
        if (state.GameDirectory is not { } game
            || state.LogSummary.HasCrash
            || state.LogSummary.ErrorCount > 0)
        {
            return null;
        }

        var directory = Path.Combine(AppPaths.LastKnownGood, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(directory);
        var snapshot = new LastKnownGoodSnapshot
        {
            CreatedAt = DateTime.Now,
            DirectoryPath = directory,
            SmapiVersion = state.LogSummary.SmapiVersion,
            GameVersion = state.LogSummary.GameVersion,
            EnabledModsZip = await CreateOptionalZipAsync(Path.Combine(game.Path, "Mods"), Path.Combine(directory, "Mods.zip")),
            DisabledModsZip = await CreateOptionalZipAsync(Path.Combine(game.Path, "Mods.Disabled"), Path.Combine(directory, "Mods.Disabled.zip")),
            EnabledMods = state.Mods.Where(mod => mod.IsEnabled && !mod.IsArchived).Select(ToEntry).ToList(),
            DisabledMods = state.Mods.Where(mod => !mod.IsEnabled && !mod.IsArchived).Select(ToEntry).ToList()
        };
        if (state.CurrentSave is { ParseError: null } save)
        {
            snapshot.SaveBackupPath = (await saveBackups.CreateAsync(save, "稳定状态关联备份")).ZipPath;
        }

        await JsonHelper.WriteAsync(Path.Combine(directory, "snapshot.json"), snapshot);
        await logging.InfoAsync("已保存上次可正常游玩状态");
        return snapshot;
    }

    public async Task<LastKnownGoodSnapshot?> GetLatestAsync()
    {
        if (!Directory.Exists(AppPaths.LastKnownGood))
        {
            return null;
        }

        foreach (var path in Directory.EnumerateFiles(AppPaths.LastKnownGood, "snapshot.json", SearchOption.AllDirectories)
                     .OrderByDescending(File.GetCreationTimeUtc))
        {
            try
            {
                return await JsonHelper.ReadAsync<LastKnownGoodSnapshot>(path);
            }
            catch (Exception exception)
            {
                await logging.ErrorAsync("读取稳定状态快照失败", exception);
            }
        }

        return null;
    }

    public async Task RestoreAsync(LastKnownGoodSnapshot snapshot)
    {
        var game = state.GameDirectory ?? throw new InvalidOperationException("尚未连接游戏目录。");
        runLock.Refresh();
        if (state.IsGameRunning)
        {
            throw new InvalidOperationException("游戏正在运行，不能恢复模组状态。");
        }

        var mods = Path.Combine(game.Path, "Mods");
        var disabled = Path.Combine(game.Path, "Mods.Disabled");
        var recovery = Path.Combine(AppPaths.FailedStates, $"lkg_recovery_{DateTime.Now:yyyyMMdd_HHmmss}");
        string? currentEnabled = null;
        string? currentDisabled = null;
        string? currentSaveBackup = null;
        var success = false;
        var rolledBack = false;
        string? error = null;
        await AppendRestoreRecordAsync(snapshot, recovery, null, null, null, false, false, null, "Started");
        try
        {
            Directory.CreateDirectory(recovery);
            currentEnabled = await CreateOptionalZipAsync(mods, Path.Combine(recovery, "Mods.zip"));
            currentDisabled = await CreateOptionalZipAsync(disabled, Path.Combine(recovery, "Mods.Disabled.zip"));
            if (state.CurrentSave is { ParseError: null } save)
            {
                currentSaveBackup = (await saveBackups.CreateAsync(save, "稳定状态恢复前自动备份")).ZipPath;
            }

            state.HasPendingRecovery = true;
            ReplaceRoot(mods, snapshot.EnabledModsZip, recovery);
            ReplaceRoot(disabled, snapshot.DisabledModsZip, recovery);
            var restored = await modScanner.ScanAsync();
            VerifyRestoredState(snapshot, restored);
            state.HasPendingRecovery = false;
            success = true;
            await logging.InfoAsync("已恢复上次可正常游玩状态");
        }
        catch (Exception exception)
        {
            error = exception.Message;
            if (state.HasPendingRecovery)
            {
                try
                {
                    ReplaceRoot(mods, currentEnabled, recovery);
                    ReplaceRoot(disabled, currentDisabled, recovery);
                    state.HasPendingRecovery = false;
                    rolledBack = true;
                }
                catch (Exception rollbackException)
                {
                    await logging.ErrorAsync("稳定状态恢复回滚失败", rollbackException);
                }
            }

            await logging.ErrorAsync("恢复稳定状态失败", exception);
            throw;
        }
        finally
        {
            await AppendRestoreRecordAsync(
                snapshot,
                recovery,
                currentEnabled,
                currentDisabled,
                currentSaveBackup,
                success,
                rolledBack,
                error,
                success ? "Succeeded" : rolledBack ? "RolledBack" : "Failed");
        }
    }

    private static Task AppendRestoreRecordAsync(
        LastKnownGoodSnapshot snapshot,
        string recovery,
        string? currentEnabled,
        string? currentDisabled,
        string? currentSaveBackup,
        bool success,
        bool rolledBack,
        string? error,
        string status)
    {
        var record = JsonSerializer.Serialize(new
        {
            Operation = "恢复稳定状态",
            Snapshot = snapshot.DirectoryPath,
            RecoveryDirectory = recovery,
            CurrentEnabledBackup = currentEnabled,
            CurrentDisabledBackup = currentDisabled,
            CurrentSaveBackup = currentSaveBackup,
            Status = status,
            Success = success,
            RolledBack = rolledBack,
            Error = error,
            CreatedAt = DateTime.Now
        }, JsonHelper.Options).Replace(Environment.NewLine, string.Empty);
        Directory.CreateDirectory(AppPaths.FailedStates);
        return File.AppendAllTextAsync(
            Path.Combine(AppPaths.FailedStates, "last-known-good-transactions.jsonl"),
            record + Environment.NewLine);
    }

    private async Task<string?> CreateOptionalZipAsync(string source, string destination)
    {
        if (!Directory.Exists(source) || !Directory.EnumerateFileSystemEntries(source).Any())
        {
            return null;
        }

        await files.CreateVerifiedZipAsync(source, destination);
        return destination;
    }

    private static void ReplaceRoot(string target, string? zip, string recoveryDirectory)
    {
        if (Directory.Exists(target))
        {
            var displaced = Path.Combine(recoveryDirectory, $"{Path.GetFileName(target)}_displaced_{Guid.NewGuid():N}");
            Directory.Move(target, displaced);
        }

        Directory.CreateDirectory(target);
        if (zip is not null)
        {
            ZipFile.ExtractToDirectory(zip, target, true);
        }
    }

    private static void VerifyRestoredState(LastKnownGoodSnapshot snapshot, IReadOnlyList<ModInfo> restored)
    {
        var expectedEnabled = snapshot.EnabledMods
            .Select(mod => mod.UniqueId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actualEnabled = restored
            .Where(mod => mod.IsEnabled && !mod.IsArchived && !string.IsNullOrWhiteSpace(mod.UniqueId))
            .Select(mod => mod.UniqueId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedDisabled = snapshot.DisabledMods
            .Select(mod => mod.UniqueId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actualDisabled = restored
            .Where(mod => !mod.IsEnabled && !mod.IsArchived && !string.IsNullOrWhiteSpace(mod.UniqueId))
            .Select(mod => mod.UniqueId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!expectedEnabled.SetEquals(actualEnabled) || !expectedDisabled.SetEquals(actualDisabled))
        {
            throw new InvalidDataException("恢复后的模组组合未通过验证，已开始回滚。");
        }
    }

    private static SnapshotModEntry ToEntry(ModInfo mod)
    {
        return new SnapshotModEntry
        {
            Name = mod.Name,
            UniqueId = mod.UniqueId,
            Version = mod.Version
        };
    }
}
