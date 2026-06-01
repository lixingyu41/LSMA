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

    public Task CreateVerifiedZipAsync(
        string sourceDirectory,
        string destinationPath,
        bool includeBaseDirectory = false,
        CancellationToken cancellationToken = default)
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

                ZipFile.CreateFromDirectory(sourceDirectory, temporary, CompressionLevel.Fastest, includeBaseDirectory);
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

    public async Task MoveDirectoryAsync(string source, string destination, string permittedDestinationRoot)
    {
        EnsureInside(destination, permittedDestinationRoot);
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException("源目录不存在。");
        }

        await Task.Run(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (Directory.Exists(destination))
            {
                throw new IOException("目标目录已存在。");
            }

            if (SameVolume(source, destination))
            {
                Directory.Move(source, destination);
                return;
            }

            var staging = $"{destination}.moving_{Guid.NewGuid():N}";
            try
            {
                CopyDirectory(source, staging);
                VerifyDirectoryCopy(source, staging);
                Directory.Move(staging, destination);
                Directory.Delete(source, true);
            }
            finally
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, true);
                }
            }
        });
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
            return MoveDirectoryAsync(sourceDirectory, destination, AppPaths.FailedStates);
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

    private static bool SameVolume(string left, string right)
    {
        var leftRoot = Path.GetPathRoot(Path.GetFullPath(left));
        var rightRoot = Path.GetPathRoot(Path.GetFullPath(right));
        return string.Equals(leftRoot, rightRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, false);
        }
    }

    private static void VerifyDirectoryCopy(string source, string destination)
    {
        var sourceFiles = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Select(path => (Relative: Path.GetRelativePath(source, path), Length: new FileInfo(path).Length))
            .OrderBy(file => file.Relative, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var destinationFiles = Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories)
            .Select(path => (Relative: Path.GetRelativePath(destination, path), Length: new FileInfo(path).Length))
            .OrderBy(file => file.Relative, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (sourceFiles.Count != destinationFiles.Count)
        {
            throw new IOException("跨盘移动校验失败：文件数量不一致。");
        }

        for (var index = 0; index < sourceFiles.Count; index++)
        {
            if (!string.Equals(sourceFiles[index].Relative, destinationFiles[index].Relative, StringComparison.OrdinalIgnoreCase)
                || sourceFiles[index].Length != destinationFiles[index].Length)
            {
                throw new IOException("跨盘移动校验失败：文件内容不一致。");
            }
        }
    }
}
