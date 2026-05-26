using LSMA.Utilities;

namespace LSMA.Services;

public sealed class LoggingService
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task InfoAsync(string message)
    {
        await WriteAsync("INFO", message);
    }

    public async Task ErrorAsync(string message, Exception? exception = null)
    {
        var detail = exception is null ? message : $"{message}: {exception.Message}";
        await WriteAsync("ERROR", detail);
    }

    private async Task WriteAsync(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Logs);
            await _lock.WaitAsync();
            await File.AppendAllTextAsync(
                AppPaths.LogFile,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
        }
        finally
        {
            if (_lock.CurrentCount == 0)
            {
                _lock.Release();
            }
        }
    }
}
