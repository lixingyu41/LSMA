namespace LSMA.Services;

public sealed record AutomaticScanWatchTarget(
    string Path,
    bool IncludeSubdirectories,
    Func<string, bool>? AcceptPath = null);

public sealed class AutomaticScanMonitor(
    UiDispatcherService dispatcher,
    Func<Task> refreshAsync) : IDisposable
{
    private const int DebounceMilliseconds = 350;
    private readonly object _sync = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private CancellationTokenSource? _refreshCancellation;

    public void ReplaceWatchers(params AutomaticScanWatchTarget[] targets)
    {
        lock (_sync)
        {
            DisposeWatchers();
            foreach (var target in targets.Where(target => Directory.Exists(target.Path)))
            {
                var watcher = new FileSystemWatcher(target.Path)
                {
                    IncludeSubdirectories = target.IncludeSubdirectories,
                    NotifyFilter = NotifyFilters.FileName
                        | NotifyFilters.DirectoryName
                        | NotifyFilters.LastWrite
                        | NotifyFilters.Size
                };
                watcher.Changed += (_, args) => Changed(target, args.FullPath);
                watcher.Created += (_, args) => Changed(target, args.FullPath);
                watcher.Deleted += (_, args) => Changed(target, args.FullPath);
                watcher.Renamed += (_, args) =>
                {
                    Changed(target, args.OldFullPath);
                    Changed(target, args.FullPath);
                };
                watcher.Error += (_, _) => RequestRefresh();
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
        }
    }

    public void RequestRefresh()
    {
        CancellationToken token;
        lock (_sync)
        {
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            _refreshCancellation = new CancellationTokenSource();
            token = _refreshCancellation.Token;
        }

        _ = RefreshAfterDelayAsync(token);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            _refreshCancellation = null;
            DisposeWatchers();
        }
    }

    private void Changed(AutomaticScanWatchTarget target, string path)
    {
        if (target.AcceptPath is null || target.AcceptPath(path))
        {
            RequestRefresh();
        }
    }

    private async Task RefreshAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(DebounceMilliseconds, token);
            dispatcher.Enqueue(() => _ = refreshAsync());
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void DisposeWatchers()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
    }
}
