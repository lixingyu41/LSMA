using System.IO.Compression;
using LSMA.Utilities;

namespace LSMA.Services;

public sealed class FileSystemSafeService(LoggingService logging)
{
    public void EnsureInside(string path, string permittedRoot)
    {
        var resolvedPath = Path.GetFullPath(path);
        var resolvedRoot = Path.GetFullPath(permittedRoot).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!resolvedPath.StartsWith(resolvedRoot, StringComparison.OrdinalIgnoreCase)
            && !resolvedPath.Equals(Path.GetFullPath(permittedRoot), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标位置不在允许的数据目录内。");
        }
    }

    public Task CreateVerifiedZipAsync(string sourceDirectory, string destinationPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(sourceDirectory))
            {
                throw new DirectoryNotFoundException("要备份的目录不存在。");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            var temporary = destinationPath + ".partial";
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }

                ZipFile.CreateFromDirectory(sourceDirectory, temporary, CompressionLevel.Fastest, false);
                using (var archive = ZipFile.OpenRead(temporary))
                {
                    if (archive.Entries.Count == 0)
                    {
                        throw new InvalidDataException("创建的恢复点为空。");
                    }
                }

                File.Move(temporary, destinationPath, true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }, cancellationToken);
    }

    public Task MoveDirectoryAsync(string source, string destination, string permittedDestinationRoot)
    {
        EnsureInside(destination, permittedDestinationRoot);
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException("源目录不存在。");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Directory.Move(source, destination);
        return Task.CompletedTask;
    }

    public async Task RestoreDirectoryFromZipAsync(string zipPath, string targetDirectory, string permittedTargetRoot)
    {
        EnsureInside(targetDirectory, permittedTargetRoot);
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("恢复点不存在。", zipPath);
        }

        await Task.Run(() =>
        {
            var failedCopy = Path.Combine(
                AppPaths.FailedStates,
                $"{FileSystemHelper.SafeFilePart(Path.GetFileName(targetDirectory))}_{DateTime.Now:yyyyMMdd_HHmmss_fff}");
            if (Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(AppPaths.FailedStates);
                Directory.Move(targetDirectory, failedCopy);
            }

            try
            {
                ZipFile.ExtractToDirectory(zipPath, targetDirectory, true);
            }
            catch
            {
                if (Directory.Exists(targetDirectory))
                {
                    Directory.Delete(targetDirectory, true);
                }

                if (Directory.Exists(failedCopy))
                {
                    Directory.Move(failedCopy, targetDirectory);
                }

                throw;
            }
        });
    }

    public Task QuarantineDirectoryAsync(string sourceDirectory, string name)
    {
        var destination = Path.Combine(
            AppPaths.FailedStates,
            $"{FileSystemHelper.SafeFilePart(name)}_{DateTime.Now:yyyyMMdd_HHmmss_fff}");
        EnsureInside(destination, AppPaths.FailedStates);
        Directory.CreateDirectory(AppPaths.FailedStates);
        if (Directory.Exists(sourceDirectory))
        {
            Directory.Move(sourceDirectory, destination);
        }

        return Task.CompletedTask;
    }

    public Task DeleteDirectoryAsync(string path, string permittedRoot)
    {
        EnsureInside(path, permittedRoot);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }

        return Task.CompletedTask;
    }

    public Task ClearDirectoryAsync(string directory)
        => ClearDirectoryAsync(directory, AppPaths.Cache);

    public async Task ClearDirectoryAsync(string directory, string permittedRoot)
    {
        EnsureInside(directory, permittedRoot);
        await Task.Run(() =>
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                return;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                File.Delete(file);
            }

            foreach (var child in Directory.EnumerateDirectories(directory)
                         .OrderByDescending(path => path.Length))
            {
                Directory.Delete(child, true);
            }
        });
        await logging.InfoAsync("已安全清理缓存目录");
    }
}
