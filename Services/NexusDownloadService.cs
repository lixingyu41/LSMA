using LSMA.Models;
using LSMA.Utilities;

namespace LSMA.Services;

public sealed class NexusDownloadService(NexusClient nexus, LoggingService logging)
{
    private readonly HttpClient _downloads = new() { Timeout = TimeSpan.FromMinutes(10) };

    public async Task<string> DownloadAsync(DownloadQueueItem item, string apiKey, CancellationToken cancellationToken = default)
    {
        item.State = DownloadState.Downloading;
        try
        {
            var url = await nexus.GetDownloadUrlAsync(item.ModId, item.FileId, apiKey);
            var safeName = FileSystemHelper.SafeFilePart(string.IsNullOrWhiteSpace(item.FileName) ? $"{item.ModName}.zip" : item.FileName);
            if (!safeName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                safeName += ".zip";
            }

            var target = Path.Combine(AppPaths.Downloads, safeName);
            Directory.CreateDirectory(AppPaths.Downloads);
            using var response = await _downloads.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var output = File.Create(target))
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            using var check = File.OpenRead(target);
            if (check.Length == 0)
            {
                throw new InvalidDataException("下载文件为空。");
            }

            item.LocalPath = target;
            item.State = DownloadState.AwaitingInstall;
            await logging.InfoAsync($"Nexus 文件已下载：{item.ModName}");
            return target;
        }
        catch (Exception exception)
        {
            item.State = exception is OperationCanceledException ? DownloadState.Canceled : DownloadState.Failed;
            item.Error = exception.Message;
            await logging.ErrorAsync($"Nexus 下载失败：{item.ModName}", exception);
            throw;
        }
    }
}
