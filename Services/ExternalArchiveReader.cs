using System.IO.Compression;
using LSMA.Utilities;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace LSMA.Services;

public sealed class ExternalArchiveReader(LoggingService logging)
{
    public async Task<string> ConvertToZipAsync(string archivePath)
    {
        var extraction = Path.Combine(AppPaths.Temp, $"external_{Guid.NewGuid():N}");
        var normalized = Path.Combine(AppPaths.Temp, $"external_{Guid.NewGuid():N}.zip");
        Directory.CreateDirectory(extraction);
        try
        {
            await Task.Run(() => ExtractArchive(archivePath, extraction));
            if (!Directory.EnumerateFileSystemEntries(extraction).Any())
            {
                throw new InvalidDataException("压缩包为空或无法读取。");
            }

            ZipFile.CreateFromDirectory(extraction, normalized, CompressionLevel.Fastest, false);
            await logging.InfoAsync("已读取非 ZIP 模组压缩包");
            return normalized;
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync("压缩包读取失败", exception);
            throw;
        }
        finally
        {
            if (Directory.Exists(extraction))
            {
                Directory.Delete(extraction, true);
            }
        }
    }

    private static void ExtractArchive(string archivePath, string extraction)
    {
        var extractionRoot = Path.GetFullPath(extraction) + Path.DirectorySeparatorChar;
        using var archive = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions());
        foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
        {
            var key = entry.Key ?? throw new InvalidDataException("压缩包包含无效文件路径。");
            var relativePath = key.Replace('/', Path.DirectorySeparatorChar);
            var targetPath = Path.GetFullPath(Path.Combine(extraction, relativePath));
            if (!targetPath.StartsWith(extractionRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("压缩包包含越界文件路径。");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            using var output = File.Create(targetPath);
            using var input = entry.OpenEntryStream();
            input.CopyTo(output);
        }
    }
}
