using LSMA.Models;
using LSMA.Utilities;

namespace LSMA.Services;

public sealed class SettingsService(LoggingService logging)
{
    public AppSettings Current { get; private set; } = new();

    public async Task InitializeAsync()
    {
        foreach (var path in AppPaths.RequiredDirectories)
        {
            Directory.CreateDirectory(path);
        }

        if (!File.Exists(AppPaths.SettingsFile))
        {
            await SaveAsync();
            return;
        }

        try
        {
            Current = await JsonHelper.ReadAsync<AppSettings>(AppPaths.SettingsFile) ?? new AppSettings();
            var needsSave = Current.SchemaVersion < 9;
            Current.SchemaVersion = 9;
            if (Current.NexusBindings is null)
            {
                Current.NexusBindings = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                needsSave = true;
            }
            else
            {
                Current.NexusBindings = Current.NexusBindings
                    .GroupBy(binding => binding.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);
            }

            if (Current.ModUpdateCache is null)
            {
                Current.ModUpdateCache = new Dictionary<string, ModUpdateCacheEntry>(StringComparer.OrdinalIgnoreCase);
                needsSave = true;
            }
            else
            {
                Current.ModUpdateCache = Current.ModUpdateCache
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
                    .GroupBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);
            }

            if (!double.IsFinite(Current.ModsPageListRatio)
                || Current.ModsPageListRatio < 0
                || Current.ModsPageListRatio > 1)
            {
                Current.ModsPageListRatio = 0;
                needsSave = true;
            }

            var modRetention = Math.Clamp(Current.ModBackupRetention, 1, 200);
            var saveRetention = Math.Clamp(Current.SaveBackupRetention, 1, 200);
            if (modRetention != Current.ModBackupRetention || saveRetention != Current.SaveBackupRetention)
            {
                Current.ModBackupRetention = modRetention;
                Current.SaveBackupRetention = saveRetention;
                needsSave = true;
            }

            if (needsSave)
            {
                await SaveAsync();
            }
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync("读取设置失败，已使用默认配置", exception);
            Current = new AppSettings();
        }
    }

    public async Task UpdateAsync(Action<AppSettings> update)
    {
        update(Current);
        await SaveAsync();
    }

    public async Task SaveAsync()
    {
        try
        {
            await JsonHelper.WriteAsync(AppPaths.SettingsFile, Current);
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync("保存设置失败", exception);
            throw;
        }
    }
}
