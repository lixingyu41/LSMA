using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LSMA.Models;
using LSMA.Utilities;

namespace LSMA.Services;

public sealed class NexusModNameTranslationService(
    SettingsService settings,
    MyMemoryTranslationClient client,
    LoggingService logging,
    UiDispatcherService dispatcher)
{
    private const int CacheVersion = 1;
    private const int DailyCharacterLimit = 4500;
    private const int MaxRequestBytes = 500;
    private static readonly TimeSpan RequestDelay = TimeSpan.FromSeconds(2);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Queue<TranslationWorkItem> _queue = new();
    private readonly HashSet<long> _queuedModIds = [];
    private readonly Dictionary<long, List<NexusModInfo>> _pendingMods = [];
    private NexusNameTranslationCacheFile? _cache;
    private bool _workerRunning;
    private bool _quotaStopped;

    public async Task ApplyCachedAndQueueAsync(IReadOnlyList<NexusModInfo> mods, CancellationToken cancellationToken = default)
    {
        if (mods.Count == 0)
        {
            return;
        }

        try
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                if (!settings.Current.ModMetadataTranslationEnabled)
                {
                    foreach (var mod in mods)
                    {
                        ApplyTranslation(mod, null);
                    }

                    return;
                }

                var cache = await LoadCacheAsync(cancellationToken);
                ResetDailyUsage(cache);
                foreach (var mod in mods)
                {
                    QueueOrApplyLocked(mod, cache);
                }

                StartWorkerIfNeeded();
            }
            finally
            {
                _lock.Release();
            }
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync("下载页模组名翻译入队失败", exception);
        }
    }

    private void QueueOrApplyLocked(NexusModInfo mod, NexusNameTranslationCacheFile cache)
    {
        var name = mod.Name.Trim();
        if (mod.ModId <= 0 || string.IsNullOrWhiteSpace(name) || HasMostlyChineseText(name))
        {
            return;
        }

        var key = mod.ModId.ToString(CultureInfo.InvariantCulture);
        var nameHash = HashSource(name);
        if (cache.Entries.TryGetValue(key, out var entry)
            && string.Equals(entry.NameHash, nameHash, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(entry.TranslatedName))
        {
            ApplyTranslation(mod, entry.TranslatedName);
            return;
        }

        if (_quotaStopped || Encoding.UTF8.GetByteCount(name) > MaxRequestBytes)
        {
            return;
        }

        if (!_pendingMods.TryGetValue(mod.ModId, out var pending))
        {
            pending = [];
            _pendingMods[mod.ModId] = pending;
        }

        if (!pending.Contains(mod))
        {
            pending.Add(mod);
        }

        if (_queuedModIds.Add(mod.ModId))
        {
            _queue.Enqueue(new TranslationWorkItem(mod.ModId, name, nameHash));
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
            TranslationWorkItem item;
            try
            {
                await _lock.WaitAsync();
                try
                {
                    if (_queue.Count == 0 || _quotaStopped || !settings.Current.ModMetadataTranslationEnabled)
                    {
                        _workerRunning = false;
                        return;
                    }

                    item = _queue.Dequeue();
                    _queuedModIds.Remove(item.ModId);
                }
                finally
                {
                    _lock.Release();
                }

                await Task.Delay(RequestDelay);
                await TranslateOneAsync(item);
            }
            catch (Exception exception)
            {
                await logging.ErrorAsync("下载页模组名翻译失败，本轮停止在线翻译", exception);
                await StopWorkerAsync();
                return;
            }
        }
    }

    private async Task TranslateOneAsync(TranslationWorkItem item)
    {
        NexusNameTranslationCacheFile cache;
        await _lock.WaitAsync();
        try
        {
            cache = await LoadCacheAsync(CancellationToken.None);
            ResetDailyUsage(cache);
            if (!EnsureDailyQuota(cache, item.SourceName.Length))
            {
                _quotaStopped = true;
                await logging.ErrorAsync($"MyMemory 下载页模组名翻译额度 {DailyCharacterLimit} chars/day 已达到本地限制");
                return;
            }
        }
        finally
        {
            _lock.Release();
        }

        var translated = (await client.TranslateSegmentAsync(item.SourceName)).Trim();
        if (string.IsNullOrWhiteSpace(translated))
        {
            translated = item.SourceName;
        }

        await _lock.WaitAsync();
        try
        {
            cache = await LoadCacheAsync(CancellationToken.None);
            cache.DailyCharactersUsed += item.SourceName.Length;
            cache.Entries[item.ModId.ToString(CultureInfo.InvariantCulture)] = new NexusNameTranslationCacheEntry
            {
                NameHash = item.NameHash,
                TranslatedName = translated,
                UpdatedAt = DateTimeOffset.Now,
            };
            await SaveCacheAsync(cache, CancellationToken.None);

            if (_pendingMods.Remove(item.ModId, out var pendingMods))
            {
                foreach (var mod in pendingMods)
                {
                    ApplyTranslation(mod, translated);
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task StopWorkerAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _queue.Clear();
            _queuedModIds.Clear();
            _pendingMods.Clear();
            _workerRunning = false;
            _quotaStopped = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<NexusNameTranslationCacheFile> LoadCacheAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        if (!File.Exists(AppPaths.NexusModNameTranslationCacheFile))
        {
            _cache = new NexusNameTranslationCacheFile();
            return _cache;
        }

        try
        {
            _cache = await JsonHelper.ReadAsync<NexusNameTranslationCacheFile>(
                    AppPaths.NexusModNameTranslationCacheFile,
                    cancellationToken)
                ?? new NexusNameTranslationCacheFile();
            _cache.Version = CacheVersion;
            _cache.Entries = new Dictionary<string, NexusNameTranslationCacheEntry>(
                _cache.Entries,
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync("读取下载页模组名翻译缓存失败，已使用空缓存", exception);
            _cache = new NexusNameTranslationCacheFile();
        }

        return _cache;
    }

    private void ResetDailyUsage(NexusNameTranslationCacheFile cache)
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (string.Equals(cache.DailyQuotaDate, today, StringComparison.Ordinal))
        {
            return;
        }

        cache.DailyQuotaDate = today;
        cache.DailyCharactersUsed = 0;
        _quotaStopped = false;
    }

    private static bool EnsureDailyQuota(NexusNameTranslationCacheFile cache, int characters)
        => cache.DailyCharactersUsed + characters <= DailyCharacterLimit;

    private void ApplyTranslation(NexusModInfo mod, string? translatedName)
        => dispatcher.Enqueue(() => mod.TranslatedName = translatedName);

    private static string HashSource(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static bool HasMostlyChineseText(string text)
    {
        var considered = 0;
        var chinese = 0;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            var isChinese = IsChinese(character);
            if (isChinese || char.IsLetterOrDigit(character))
            {
                considered++;
                if (isChinese)
                {
                    chinese++;
                }
            }
        }

        return considered > 0 && (double)chinese / considered > 0.4;
    }

    private static bool IsChinese(char character)
        => character is >= '\u3400' and <= '\u4DBF'
            or >= '\u4E00' and <= '\u9FFF'
            or >= '\uF900' and <= '\uFAFF';

    private static Task SaveCacheAsync(NexusNameTranslationCacheFile cache, CancellationToken cancellationToken)
        => JsonHelper.WriteAsync(AppPaths.NexusModNameTranslationCacheFile, cache, cancellationToken);

    private readonly record struct TranslationWorkItem(long ModId, string SourceName, string NameHash);

    private sealed class NexusNameTranslationCacheFile
    {
        public int Version { get; set; } = CacheVersion;
        public string TargetLanguage { get; set; } = "zh-CN";
        public string DailyQuotaDate { get; set; } = string.Empty;
        public int DailyCharactersUsed { get; set; }
        public Dictionary<string, NexusNameTranslationCacheEntry> Entries { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class NexusNameTranslationCacheEntry
    {
        public string NameHash { get; set; } = string.Empty;
        public string? TranslatedName { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
