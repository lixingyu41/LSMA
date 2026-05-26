using System.Diagnostics;
using System.IO.Compression;
using LSMA.Utilities;

namespace LSMA.Services;

public sealed class ExternalArchiveReader(SettingsService settings, LoggingService logging)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(settings.Current.ExternalArchiveToolPath)
        && File.Exists(settings.Current.ExternalArchiveToolPath);

    public async Task<string> ConvertToZipAsync(string archivePath)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("需要先在设置中配置 7z/RAR 解压工具。");
        }

        var extraction = Path.Combine(AppPaths.Temp, $"external_{Guid.NewGuid():N}");
        var normalized = Path.Combine(AppPaths.Temp, $"external_{Guid.NewGuid():N}.zip");
        Directory.CreateDirectory(extraction);
        var arguments = settings.Current.ExternalArchiveArgumentsTemplate
            .Replace("{input}", archivePath, StringComparison.Ordinal)
            .Replace("{output}", extraction, StringComparison.Ordinal);
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = settings.Current.ExternalArchiveToolPath!,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            }) ?? throw new InvalidOperationException("无法启动外部解压工具。");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0 || !Directory.EnumerateFileSystemEntries(extraction).Any())
            {
                throw new InvalidDataException("外部解压工具未能读取该压缩包。");
            }

            ZipFile.CreateFromDirectory(extraction, normalized, CompressionLevel.Fastest, false);
            await logging.InfoAsync("已通过外部工具读取模组压缩包");
            return normalized;
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync("外部压缩包读取失败", exception);
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
}
