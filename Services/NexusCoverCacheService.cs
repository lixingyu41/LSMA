using LSMA.Models;
using LSMA.Utilities;

namespace LSMA.Services;

public sealed class NexusCoverCacheService(
    NexusClient nexus,
    NexusCredentialService credentials,
    LoggingService logging,
    UiDispatcherService dispatcher)
{
    private const int CacheVersion = 1;
    private const long MaxImageBytes = 20 * 1024 * 1024;
    private static readonly TimeSpan SuccessRefreshInterval = TimeSpan.FromDays(7);
    private static readonly TimeSpan FailureRetryInterval = TimeSpan.FromDays(1);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private readonly SemaphoreSlim _downloadGate = new(2, 2);
    private readonly Queue<long> _queue = new();
    private readonly HashSet<long> _queuedModIds = [];
    private readonly Dictionary<long, List<NexusModInfo>> _pendingOnlineTargets = [];
    private readonly Dictionary<long, List<ModInfo>> _pendingLocalTargets = [];
    private readonly Dictionary<long, NexusCoverSnapshot> _snapshots = [];
    private NexusCoverCacheFile? _cache;
    private bool _workerRunning;

    public async Task ApplyCachedAndQueueAsync(IReadOnlyList<NexusModInfo> mods, CancellationToken cancellationToken = default)
    {
        if (mods.Count == 0)
        {
            return;
        }

        await _cacheGate.WaitAsync(cancellationToken);
        try
        {
            var cache = await LoadCacheAsync(cancellationToken);
            var changed = false;
            foreach (var mod in mods.Where(mod => mod.ModId > 0))
            {
                var key = CacheKey(mod.ModId);
                cache.Entries.TryGetValue(key, out var entry);
                if (entry is null && HasMetadata(mod))
                {
                    entry = CreateEntry(mod);
                    cache.Entries[key] = entry;
                    changed = true;
                }
                else if (entry is not null)
                {
                    changed |= MergeOnlineMetadata(entry, mod);
                    ApplyEntry(mod, entry);
                }

                if (ShouldQueue(entry, mod.CoverSourceUrl))
                {
                    QueueLocked(mod.ModId, NexusCoverSnapshot.From(mod), onlineTarget: mod, localTarget: null);
                }
            }

            if (changed)
            {
                await SaveCacheAsync(cache, cancellationToken);
            }

            StartWorkerIfNeeded();
        }
        finally
        {
            _cacheGate.Release();
        }
    }

    public async Task ApplyCachedAndQueueAsync(IReadOnlyList<ModInfo> mods, CancellationToken cancellationToken = default)
    {
        if (mods.Count == 0)
        {
            return;
        }

        await _cacheGate.WaitAsync(cancellationToken);
        try
        {
            var cache = await LoadCacheAsync(cancellationToken);
            foreach (var mod in mods.Where(mod => mod.NexusModId is > 0))
            {
                var modId = mod.NexusModId!.Value;
                cache.Entries.TryGetValue(CacheKey(modId), out var entry);
                if (entry is not null)
                {
                    ApplyEntry(mod, entry);
                }

                if (ShouldQueue(entry, preferredSourceUrl: null, allowWithoutSource: true))
                {
                    QueueLocked(modId, snapshot: null, onlineTarget: null, localTarget: mod);
                }
            }

            StartWorkerIfNeeded();
        }
        finally
        {
            _cacheGate.Release();
        }
    }

    private void QueueLocked(long modId, NexusCoverSnapshot? snapshot, NexusModInfo? onlineTarget, ModInfo? localTarget)
    {
        if (snapshot is not null)
        {
            _snapshots[modId] = snapshot;
        }

        if (onlineTarget is not null)
        {
            AddPending(_pendingOnlineTargets, modId, onlineTarget);
        }

        if (localTarget is not null)
        {
            AddPending(_pendingLocalTargets, modId, localTarget);
        }

        if (_queuedModIds.Add(modId))
        {
            _queue.Enqueue(modId);
        }
    }

    private static void AddPending<T>(Dictionary<long, List<T>> targets, long modId, T target)
        where T : class
    {
        if (!targets.TryGetValue(modId, out var values))
        {
            values = [];
            targets[modId] = values;
        }

        if (!values.Contains(target))
        {
            values.Add(target);
        }
    }

    private void StartWorkerIfNeeded()
    {
        if (_workerRunning || _queue.Count == 0)
        {
            return;
        }

        _workerRunning = true;
        _ = Task.Run(ProcessQueueAsync);
    }

    private async Task ProcessQueueAsync()
    {
        while (true)
        {
            long modId;
            try
            {
                await _cacheGate.WaitAsync();
                try
                {
                    if (_queue.Count == 0)
                    {
                        _workerRunning = false;
                        return;
                    }

                    modId = _queue.Dequeue();
                }
                finally
                {
                    _cacheGate.Release();
                }

                await RefreshOneAsync(modId);
            }
            catch (Exception exception)
            {
                await logging.ErrorAsync("Nexus 封面缓存队列失败", exception);
            }
        }
    }

    private async Task RefreshOneAsync(long modId)
    {
        NexusCoverSnapshot? snapshot;
        NexusCoverCacheEntry? current;
        await _cacheGate.WaitAsync();
        try
        {
            var cache = await LoadCacheAsync(CancellationToken.None);
            cache.Entries.TryGetValue(CacheKey(modId), out current);
            _snapshots.TryGetValue(modId, out snapshot);
        }
        finally
        {
            _cacheGate.Release();
        }

        NexusCoverCacheEntry entry;
        try
        {
            entry = await BuildEntryAsync(modId, current, snapshot);
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync($"Nexus 封面缓存刷新失败：{modId}", exception);
            entry = MarkFailure(current, modId, exception.Message);
        }

        List<NexusModInfo> onlineTargets;
        List<ModInfo> localTargets;
        await _cacheGate.WaitAsync();
        try
        {
            var cache = await LoadCacheAsync(CancellationToken.None);
            cache.Entries[CacheKey(modId)] = entry;
            await SaveCacheAsync(cache, CancellationToken.None);

            onlineTargets = _pendingOnlineTargets.Remove(modId, out var online) ? online : [];
            localTargets = _pendingLocalTargets.Remove(modId, out var local) ? local : [];
            _snapshots.Remove(modId);
            _queuedModIds.Remove(modId);
        }
        finally
        {
            _cacheGate.Release();
        }

        dispatcher.Enqueue(() =>
        {
            foreach (var target in onlineTargets)
            {
                ApplyEntry(target, entry);
            }

            foreach (var target in localTargets)
            {
                ApplyEntry(target, entry);
            }
        });
    }

    private async Task<NexusCoverCacheEntry> BuildEntryAsync(
        long modId,
        NexusCoverCacheEntry? current,
        NexusCoverSnapshot? snapshot)
    {
        NexusModInfo? remote = null;
        if (NeedsRemoteMetadata(current, snapshot))
        {
            remote = await LoadRemoteMetadataAsync(modId);
        }

        var sourceUrl = FirstNonWhiteSpace(
            snapshot?.CoverSourceUrl,
            remote?.CoverSourceUrl,
            current?.CoverSourceUrl);
        var categoryName = FirstNonWhiteSpace(
            snapshot?.CategoryName,
            remote?.CategoryName,
            current?.CategoryName);
        var updatedTimestamp = FirstPositive(
            snapshot?.UpdatedTimestamp,
            remote?.UpdatedTimestamp,
            current?.UpdatedTimestamp);
        var downloads = FirstPositive(
            snapshot?.Downloads,
            remote?.Downloads,
            current?.Downloads);
        var name = FirstNonWhiteSpace(
            snapshot?.Name,
            remote?.Name,
            current?.Name);

        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            return MarkFailure(current, modId, "Nexus 未返回封面图片。") with
            {
                Name = name,
                CategoryName = categoryName,
                UpdatedTimestamp = updatedTimestamp,
                Downloads = downloads
            };
        }

        var coverImagePath = current?.CoverImagePath;
        var shouldDownload = string.IsNullOrWhiteSpace(coverImagePath)
            || !File.Exists(coverImagePath)
            || !string.Equals(current?.CoverSourceUrl, sourceUrl, StringComparison.OrdinalIgnoreCase)
            || IsSuccessStale(current);

        if (shouldDownload)
        {
            await _downloadGate.WaitAsync();
            try
            {
                var previousPath = coverImagePath;
                coverImagePath = await DownloadCoverAsync(modId, sourceUrl);
                DeleteOldCover(previousPath, coverImagePath);
            }
            finally
            {
                _downloadGate.Release();
            }
        }

        return new NexusCoverCacheEntry
        {
            ModId = modId,
            Name = name,
            CoverSourceUrl = sourceUrl,
            CoverImagePath = coverImagePath,
            CategoryName = categoryName,
            UpdatedTimestamp = updatedTimestamp,
            Downloads = downloads,
            LastSuccessUtc = DateTimeOffset.UtcNow,
            LastFailureUtc = null,
            FailureMessage = null
        };
    }

    private async Task<NexusModInfo?> LoadRemoteMetadataAsync(long modId)
    {
        try
        {
            if (await nexus.GetModFromGraphQlAsync(modId) is { } graphMod)
            {
                return graphMod;
            }
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync($"Nexus GraphQL 封面元数据获取失败：{modId}", exception);
        }

        var apiKey = credentials.GetKey();
        return string.IsNullOrWhiteSpace(apiKey)
            ? null
            : await nexus.GetModAsync(modId, apiKey);
    }

    private async Task<string> DownloadCoverAsync(long modId, string sourceUrl)
    {
        Directory.CreateDirectory(AppPaths.NexusCoverCache);
        using var response = await _http.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var extension = GetImageExtension(sourceUrl, response.Content.Headers.ContentType?.MediaType);
        var target = Path.Combine(AppPaths.NexusCoverCache, $"{CacheKey(modId)}{extension}");
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var input = await response.Content.ReadAsStreamAsync())
            await using (var output = File.Create(temporary))
            {
                var buffer = new byte[81920];
                long total = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer);
                    if (read == 0)
                    {
                        break;
                    }

                    total += read;
                    if (total > MaxImageBytes)
                    {
                        throw new InvalidDataException("封面图片超过 20 MB。");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read));
                }

                if (total == 0)
                {
                    throw new InvalidDataException("封面图片为空。");
                }
            }

            File.Move(temporary, target, true);
            return target;
        }
        finally
        {
            try { File.Delete(temporary); } catch { /* best effort */ }
        }
    }

    private static string GetImageExtension(string sourceUrl, string? contentType)
    {
        var fromContentType = contentType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "image/jpeg" or "image/jpg" => ".jpg",
            _ => null
        };
        if (fromContentType is not null)
        {
            return fromContentType;
        }

        if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
        {
            var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
            if (extension is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif")
            {
                return extension == ".jpeg" ? ".jpg" : extension;
            }
        }

        return ".jpg";
    }

    private async Task<NexusCoverCacheFile> LoadCacheAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        if (!File.Exists(AppPaths.NexusCoverCacheFile))
        {
            _cache = new NexusCoverCacheFile();
            return _cache;
        }

        try
        {
            _cache = await JsonHelper.ReadAsync<NexusCoverCacheFile>(AppPaths.NexusCoverCacheFile, cancellationToken)
                ?? new NexusCoverCacheFile();
            _cache.Version = CacheVersion;
            _cache.Entries = new Dictionary<string, NexusCoverCacheEntry>(
                _cache.Entries,
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync("读取 Nexus 封面缓存失败，已使用空缓存", exception);
            _cache = new NexusCoverCacheFile();
        }

        return _cache;
    }

    private static Task SaveCacheAsync(NexusCoverCacheFile cache, CancellationToken cancellationToken)
        => JsonHelper.WriteAsync(AppPaths.NexusCoverCacheFile, cache, cancellationToken);

    private static bool ShouldQueue(
        NexusCoverCacheEntry? entry,
        string? preferredSourceUrl,
        bool allowWithoutSource = false)
    {
        if (entry is null)
        {
            return allowWithoutSource || !string.IsNullOrWhiteSpace(preferredSourceUrl);
        }

        if (!string.IsNullOrWhiteSpace(preferredSourceUrl)
            && !string.Equals(entry.CoverSourceUrl, preferredSourceUrl, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(entry.CoverImagePath) && !File.Exists(entry.CoverImagePath))
        {
            return !HasRecentFailure(entry);
        }

        if (entry.LastSuccessUtc is null)
        {
            return !HasRecentFailure(entry);
        }

        return IsSuccessStale(entry) && !HasRecentFailure(entry);
    }

    private static bool NeedsRemoteMetadata(NexusCoverCacheEntry? current, NexusCoverSnapshot? snapshot)
        => snapshot is null
            || string.IsNullOrWhiteSpace(snapshot.CoverSourceUrl)
            || string.IsNullOrWhiteSpace(snapshot.CategoryName)
            || snapshot.UpdatedTimestamp <= 0
            || snapshot.Downloads <= 0
            || IsSuccessStale(current);

    private static bool IsSuccessStale(NexusCoverCacheEntry? entry)
        => entry?.LastSuccessUtc is null
            || DateTimeOffset.UtcNow - entry.LastSuccessUtc.Value > SuccessRefreshInterval;

    private static bool HasRecentFailure(NexusCoverCacheEntry entry)
        => entry.LastFailureUtc is { } failure
            && DateTimeOffset.UtcNow - failure < FailureRetryInterval;

    private static NexusCoverCacheEntry MarkFailure(NexusCoverCacheEntry? current, long modId, string message)
        => new()
        {
            ModId = modId,
            Name = current?.Name,
            CoverSourceUrl = current?.CoverSourceUrl,
            CoverImagePath = current?.CoverImagePath,
            CategoryName = current?.CategoryName,
            UpdatedTimestamp = current?.UpdatedTimestamp,
            Downloads = current?.Downloads,
            LastSuccessUtc = current?.LastSuccessUtc,
            LastFailureUtc = DateTimeOffset.UtcNow,
            FailureMessage = message
        };

    private static bool HasMetadata(NexusModInfo mod)
        => !string.IsNullOrWhiteSpace(mod.CoverSourceUrl)
            || !string.IsNullOrWhiteSpace(mod.CategoryName)
            || mod.UpdatedTimestamp > 0
            || mod.Downloads > 0;

    private static NexusCoverCacheEntry CreateEntry(NexusModInfo mod)
        => new()
        {
            ModId = mod.ModId,
            Name = mod.Name,
            CoverSourceUrl = mod.CoverSourceUrl,
            CategoryName = mod.CategoryName,
            UpdatedTimestamp = mod.UpdatedTimestamp,
            Downloads = mod.Downloads
        };

    private static bool MergeOnlineMetadata(NexusCoverCacheEntry entry, NexusModInfo mod)
    {
        var changed = false;
        var name = Normalize(mod.Name);
        if (!string.Equals(entry.Name, name, StringComparison.Ordinal))
        {
            entry.Name = name;
            changed = true;
        }

        var sourceUrl = Normalize(mod.CoverSourceUrl);
        if (sourceUrl is not null
            && !string.Equals(entry.CoverSourceUrl, sourceUrl, StringComparison.Ordinal))
        {
            entry.CoverSourceUrl = sourceUrl;
            changed = true;
        }

        var categoryName = Normalize(mod.CategoryName);
        if (categoryName is not null
            && !string.Equals(entry.CategoryName, categoryName, StringComparison.Ordinal))
        {
            entry.CategoryName = categoryName;
            changed = true;
        }

        var updatedTimestamp = mod.UpdatedTimestamp > 0 ? (long?)mod.UpdatedTimestamp : null;
        if (updatedTimestamp is not null && entry.UpdatedTimestamp != updatedTimestamp)
        {
            entry.UpdatedTimestamp = updatedTimestamp;
            changed = true;
        }

        var downloads = mod.Downloads > 0 ? (long?)mod.Downloads : null;
        if (downloads is not null && entry.Downloads != downloads)
        {
            entry.Downloads = downloads;
            changed = true;
        }

        return changed;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static long? FirstPositive(params long?[] values)
        => values.FirstOrDefault(value => value is > 0);

    private static string? FirstNonWhiteSpace(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static void ApplyEntry(NexusModInfo target, NexusCoverCacheEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.CoverImagePath) && File.Exists(entry.CoverImagePath))
        {
            target.CoverImageUri = ToUri(entry.CoverImagePath);
        }

        if (string.IsNullOrWhiteSpace(target.CoverSourceUrl) && !string.IsNullOrWhiteSpace(entry.CoverSourceUrl))
        {
            target.CoverSourceUrl = entry.CoverSourceUrl;
        }

        if (string.IsNullOrWhiteSpace(target.CategoryName) && !string.IsNullOrWhiteSpace(entry.CategoryName))
        {
            target.CategoryName = entry.CategoryName;
        }

        if (target.UpdatedTimestamp <= 0 && entry.UpdatedTimestamp is > 0)
        {
            target.UpdatedTimestamp = entry.UpdatedTimestamp.Value;
        }

        if (target.Downloads <= 0 && entry.Downloads is > 0)
        {
            target.Downloads = entry.Downloads.Value;
        }
    }

    private static void ApplyEntry(ModInfo target, NexusCoverCacheEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.CoverImagePath) && File.Exists(entry.CoverImagePath))
        {
            target.CoverImageUri = ToUri(entry.CoverImagePath);
        }

        target.RemoteCategoryName = entry.CategoryName;
        target.RemoteUpdatedTimestamp = entry.UpdatedTimestamp;
        target.RemoteDownloads = entry.Downloads;
    }

    private static void DeleteOldCover(string? oldPath, string newPath)
    {
        if (string.IsNullOrWhiteSpace(oldPath)
            || string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase)
            || !IsInsideCoverCache(oldPath)
            || !File.Exists(oldPath))
        {
            return;
        }

        try { File.Delete(oldPath); } catch { /* best effort */ }
    }

    private static bool IsInsideCoverCache(string path)
    {
        var root = Path.GetFullPath(AppPaths.NexusCoverCache).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string ToUri(string path)
        => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    private static string CacheKey(long modId)
        => $"stardewvalley-{modId}";

    private sealed class NexusCoverCacheFile
    {
        public int Version { get; set; } = CacheVersion;
        public Dictionary<string, NexusCoverCacheEntry> Entries { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record NexusCoverSnapshot(
        string? Name,
        string? CoverSourceUrl,
        string? CategoryName,
        long? UpdatedTimestamp,
        long? Downloads)
    {
        public static NexusCoverSnapshot From(NexusModInfo mod)
            => new(mod.Name, mod.CoverSourceUrl, mod.CategoryName, mod.UpdatedTimestamp, mod.Downloads);
    }

    private sealed record NexusCoverCacheEntry
    {
        public long ModId { get; init; }
        public string? Name { get; set; }
        public string? CoverSourceUrl { get; set; }
        public string? CoverImagePath { get; set; }
        public string? CategoryName { get; set; }
        public long? UpdatedTimestamp { get; set; }
        public long? Downloads { get; set; }
        public DateTimeOffset? LastSuccessUtc { get; init; }
        public DateTimeOffset? LastFailureUtc { get; init; }
        public string? FailureMessage { get; init; }
    }
}
