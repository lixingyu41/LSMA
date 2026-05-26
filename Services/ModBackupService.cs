using LSMA.Models;
using LSMA.Utilities;

namespace LSMA.Services;

public sealed class ModBackupService(SettingsService settings, FileSystemSafeService files, LoggingService logging)
{
    public async Task<BackupRecord> CreateAsync(ModInfo mod, string operation)
    {
        try
        {
            return await Task.Run(async () =>
            {
                var name = FileSystemHelper.SafeFilePart(mod.Name);
                var id = FileSystemHelper.SafeFilePart(mod.Manifest?.UniqueID);
                var version = FileSystemHelper.SafeFilePart(mod.Manifest?.Version);
                var filename = $"{name}_{id}_{version}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
                var target = Path.Combine(AppPaths.ModBackups, filename);
                await files.CreateVerifiedZipAsync(mod.FolderPath, target);
                await TrimBackupsAsync();
                await logging.InfoAsync($"已为模组创建备份：{mod.Name} ({operation})");
                return new BackupRecord
                {
                    ItemName = mod.Name,
                    ZipPath = target,
                    CreatedAt = DateTime.Now,
                    Operation = operation,
                    Succeeded = true
                };
            });
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync($"创建模组恢复点失败：{mod.Name}", exception);
            throw;
        }
    }

    private Task TrimBackupsAsync()
    {
        var retention = Math.Max(1, settings.Current.ModBackupRetention);
        var excess = Directory.EnumerateFiles(AppPaths.ModBackups, "*.zip")
            .OrderByDescending(File.GetCreationTimeUtc)
            .Skip(retention)
            .ToList();
        foreach (var path in excess)
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }
}
