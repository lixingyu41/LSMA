using LSMA.Utilities;

namespace LSMA.Services;

public sealed class SaveLocatorService(LoggingService logging)
{
    public async Task<IReadOnlyList<SaveSource>> LocateAsync()
    {
        return await Task.Run(async () =>
        {
            var sources = new List<SaveSource>();
            if (!Directory.Exists(AppPaths.SaveSource))
            {
                return sources;
            }

            foreach (var directory in Directory.EnumerateDirectories(AppPaths.SaveSource)
                         .OrderByDescending(Directory.GetLastWriteTimeUtc))
            {
                try
                {
                    var folderName = Path.GetFileName(directory);
                    var main = Path.Combine(directory, folderName);
                    var fallback = Path.Combine(directory, "SaveGameInfo");
                    var path = File.Exists(main) ? main : File.Exists(fallback) ? fallback : null;
                    if (path is not null)
                    {
                        sources.Add(new SaveSource(directory, folderName, path));
                    }
                }
                catch (Exception exception)
                {
                    await logging.ErrorAsync($"定位存档失败：{directory}", exception);
                }
            }

            return sources;
        });
    }
}

public sealed record SaveSource(string DirectoryPath, string FolderName, string FilePath);
