using LSMA.Utilities;

namespace LSMA.Services;

public sealed class CacheService(FileSystemSafeService files, LoggingService logging)
{
    public async Task ClearAsync()
    {
        await Task.Run(async () =>
        {
            try
            {
                await files.ClearDirectoryAsync(AppPaths.Cache);
                await logging.InfoAsync("已清空本地缓存");
            }
            catch (Exception exception)
            {
                await logging.ErrorAsync("清空本地缓存失败", exception);
                throw;
            }
        });
    }
}
