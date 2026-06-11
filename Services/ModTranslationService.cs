using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LSMA.Models;
using LSMA.Utilities;

namespace LSMA.Services;

public sealed class ModTranslationService(
    SettingsService settings,
    MyMemoryTranslationClient client,
    LoggingService logging)
{
    private const int CacheVersion = 1;
    private const int DailyAnonymousCharacterLimit = 5000;
    private const int MaxSegmentBytes = 500;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ModTranslationCacheFile? _cache;

    public async Task ApplyAsync(IReadOnlyList<ModInfo> mods, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!settings.Current.ModMetadataTranslationEnabled)
            {
                ClearTranslations(mods);
                return;
            }

            var cache = await LoadCacheAsync(cancellationToken);
            ResetDailyUsage(cache);
            foreach (var mod in mods)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await ApplyModAsync(mod, cache, cancellationToken);
                }
                catch (ModTranslationQuotaException exception)
                {
                    await logging.ErrorAsync("模组元数据翻译额度已用尽，本轮停止在线翻译", exception);
                    break;
                }
                catch (Exception exception)
                {
                    await logging.ErrorAsync("模组元数据翻译失败，本轮停止在线翻译", exception);
                    break;
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ApplyCachedAsync(IReadOnlyList<ModInfo> mods, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!settings.Current.ModMetadataTranslationEnabled)
            {
                ClearTranslations(mods);
                return;
            }

            var cache = await LoadCacheAsync(cancellationToken);
            foreach (var mod in mods)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ApplyCachedMod(mod, cache);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task ApplyModAsync(ModInfo mod, ModTranslationCacheFile cache, CancellationToken cancellationToken)
    {
        mod.TranslatedName = null;
        mod.TranslatedDescription = null;
        var key = CacheKey(mod);
        var entry = cache.Entries.TryGetValue(key, out var existing)
            ? existing
            : new ModTranslationCacheEntry();
        cache.Entries[key] = entry;

        var name = mod.OriginalName.Trim();
        var nameHash = HashSource(name);
        if (entry.NameHash == nameHash)
        {
            mod.TranslatedName = entry.TranslatedName;
        }
        else
        {
            mod.TranslatedName = await TranslateTextAsync(name, cache, cancellationToken);
            entry.NameHash = nameHash;
            entry.TranslatedName = mod.TranslatedName;
            entry.UpdatedAt = DateTimeOffset.Now;
            await SaveCacheAsync(cache, cancellationToken);
        }

        var description = (mod.OriginalDescription ?? string.Empty).Trim();
        var descriptionHash = HashSource(description);
        if (entry.DescriptionHash == descriptionHash)
        {
            mod.TranslatedDescription = entry.TranslatedDescription;
        }
        else
        {
            mod.TranslatedDescription = await TranslateTextAsync(description, cache, cancellationToken);
            entry.DescriptionHash = descriptionHash;
            entry.TranslatedDescription = mod.TranslatedDescription;
            entry.UpdatedAt = DateTimeOffset.Now;
            await SaveCacheAsync(cache, cancellationToken);
        }
    }

    private async Task<string?> TranslateTextAsync(
        string text,
        ModTranslationCacheFile cache,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (HasMostlyChineseText(text))
        {
            return text;
        }

        var translated = new StringBuilder();
        foreach (var segment in SplitByUtf8Bytes(text, MaxSegmentBytes))
        {
            EnsureDailyQuota(cache, segment.Length);
            var value = await client.TranslateSegmentAsync(segment, cancellationToken);
            cache.DailyCharactersUsed += segment.Length;
            translated.Append(string.IsNullOrWhiteSpace(value) ? segment : value);
        }

        var result = translated.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? text : result;
    }

    private async Task<ModTranslationCacheFile> LoadCacheAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        if (!File.Exists(AppPaths.ModTranslationCacheFile))
        {
            _cache = new ModTranslationCacheFile();
            return _cache;
        }

        try
        {
            _cache = await JsonHelper.ReadAsync<ModTranslationCacheFile>(
                    AppPaths.ModTranslationCacheFile,
                    cancellationToken)
                ?? new ModTranslationCacheFile();
            _cache.Version = CacheVersion;
            _cache.Entries = new Dictionary<string, ModTranslationCacheEntry>(
                _cache.Entries,
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync("读取模组翻译缓存失败，已使用空缓存", exception);
            _cache = new ModTranslationCacheFile();
        }

        return _cache;
    }

    private static void ClearTranslations(IEnumerable<ModInfo> mods)
    {
        foreach (var mod in mods)
        {
            mod.TranslatedName = null;
            mod.TranslatedDescription = null;
        }
    }

    private static void ApplyCachedMod(ModInfo mod, ModTranslationCacheFile cache)
    {
        mod.TranslatedName = null;
        mod.TranslatedDescription = null;
        if (!cache.Entries.TryGetValue(CacheKey(mod), out var entry))
        {
            return;
        }

        var name = mod.OriginalName.Trim();
        if (entry.NameHash == HashSource(name))
        {
            mod.TranslatedName = entry.TranslatedName;
        }

        var description = (mod.OriginalDescription ?? string.Empty).Trim();
        if (entry.DescriptionHash == HashSource(description))
        {
            mod.TranslatedDescription = entry.TranslatedDescription;
        }
    }

    private static void ResetDailyUsage(ModTranslationCacheFile cache)
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!string.Equals(cache.DailyQuotaDate, today, StringComparison.Ordinal))
        {
            cache.DailyQuotaDate = today;
            cache.DailyCharactersUsed = 0;
        }
    }

    private static void EnsureDailyQuota(ModTranslationCacheFile cache, int characters)
    {
        if (cache.DailyCharactersUsed + characters > DailyAnonymousCharacterLimit)
        {
            throw new ModTranslationQuotaException(
                $"MyMemory 匿名额度 {DailyAnonymousCharacterLimit} chars/day 已达到本地限制");
        }
    }

    private static string CacheKey(ModInfo mod)
        => string.IsNullOrWhiteSpace(mod.Manifest?.UniqueID)
            ? $"folder:{mod.FolderName}"
            : mod.Manifest.UniqueID.Trim();

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

    private static IEnumerable<string> SplitByUtf8Bytes(string text, int maxBytes)
    {
        var chunk = new StringBuilder();
        var byteCount = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.ToString();
            var valueBytes = Encoding.UTF8.GetByteCount(value);
            if (chunk.Length > 0 && byteCount + valueBytes > maxBytes)
            {
                yield return chunk.ToString();
                chunk.Clear();
                byteCount = 0;
            }

            chunk.Append(value);
            byteCount += valueBytes;
        }

        if (chunk.Length > 0)
        {
            yield return chunk.ToString();
        }
    }

    private static Task SaveCacheAsync(ModTranslationCacheFile cache, CancellationToken cancellationToken)
        => JsonHelper.WriteAsync(AppPaths.ModTranslationCacheFile, cache, cancellationToken);

    private sealed class ModTranslationQuotaException(string message) : Exception(message);

    private sealed class ModTranslationCacheFile
    {
        public int Version { get; set; } = CacheVersion;
        public string TargetLanguage { get; set; } = "zh-CN";
        public string DailyQuotaDate { get; set; } = string.Empty;
        public int DailyCharactersUsed { get; set; }
        public Dictionary<string, ModTranslationCacheEntry> Entries { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ModTranslationCacheEntry
    {
        public string NameHash { get; set; } = string.Empty;
        public string? TranslatedName { get; set; }
        public string DescriptionHash { get; set; } = string.Empty;
        public string? TranslatedDescription { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
