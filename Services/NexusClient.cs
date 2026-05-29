using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LSMA.Models;
using LSMA.Utilities;

namespace LSMA.Services;

public sealed class NexusClient(LoggingService logging)
{
    private const string GameDomain = "stardewvalley";
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly HttpClient _client = new()
    {
        BaseAddress = new Uri("https://api.nexusmods.com/v1/"),
        Timeout = TimeSpan.FromSeconds(25)
    };
    private DateTime? _cooldownUntil;

    public int? HourlyRequestsRemaining { get; private set; }
    public int? DailyRequestsRemaining { get; private set; }
    public string RateLimitStatus => _cooldownUntil is { } until && until > DateTime.Now
        ? $"请求已冷却至 {until:HH:mm}"
        : HourlyRequestsRemaining is null ? "限流状态未知" : $"本小时剩余 {HourlyRequestsRemaining} 次请求";

    public async Task<NexusConnectionResult> TestConnectionAsync(string apiKey)
    {
        try
        {
            var user = await GetAsync<NexusUserInfo>("users/validate.json", apiKey);
            return new NexusConnectionResult
            {
                Success = true,
                UserName = user.Name,
                IsPremium = user.IsPremium,
                Message = user.Name is null ? "授权码验证成功，已连接 Nexus Mods。" : $"已连接 Nexus Mods：{user.Name}"
            };
        }
        catch (NexusApiException exception)
        {
            return new NexusConnectionResult { Message = exception.Message };
        }
    }

    public Task<List<NexusModInfo>> GetTrendingAsync(string apiKey, int page = 1)
        => GetAsync<List<NexusModInfo>>($"games/{GameDomain}/mods/trending.json?page={page}&size=20", apiKey);

    public Task<List<NexusModInfo>> GetLatestAddedAsync(string apiKey, int page = 1)
        => GetAsync<List<NexusModInfo>>($"games/{GameDomain}/mods/latest_added.json?page={page}&size=20", apiKey);

    public Task<List<NexusModInfo>> GetLatestUpdatedAsync(string apiKey, int page = 1)
        => GetAsync<List<NexusModInfo>>($"games/{GameDomain}/mods/latest_updated.json?page={page}&size=20", apiKey);

    public Task<NexusModInfo> GetModAsync(long modId, string apiKey)
        => GetAsync<NexusModInfo>($"games/{GameDomain}/mods/{modId}.json", apiKey);

    public Task<List<NexusCategory>> GetCategoriesAsync(string apiKey)
        => GetAsync<List<NexusCategory>>($"games/{GameDomain}/categories.json", apiKey);

    public async Task<IReadOnlyList<NexusFileInfo>> GetFilesAsync(long modId, string apiKey)
    {
        var value = await GetAsync<NexusFilesResponse>($"games/{GameDomain}/mods/{modId}/files.json", apiKey);
        return (value.Files ?? [])
            .OfType<NexusFileInfo>()
            .Where(file => file.FileId > 0)
            .OrderByDescending(file => string.Equals(file.CategoryName, "MAIN", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(file => file.FileId)
            .ToList();
    }

    public async Task<string> GetDownloadUrlAsync(long modId, long fileId, string apiKey, string? key = null, long? expires = null)
    {
        var suffix = key is not null && expires is not null
            ? $"?key={Uri.EscapeDataString(key)}&expires={expires.Value}"
            : string.Empty;
        var links = await GetAsync<List<NexusDownloadLink>>(
            $"games/{GameDomain}/mods/{modId}/files/{fileId}/download_link.json{suffix}",
            apiKey);
        return links.FirstOrDefault(link => !string.IsNullOrWhiteSpace(link.Uri))?.Uri
            ?? throw new NexusApiException("未获得可用下载地址。");
    }

    private async Task<T> GetAsync<T>(string relativePath, string apiKey)
    {
        await _requestGate.WaitAsync();
        try
        {
            if (_cooldownUntil is { } until && until > DateTime.Now)
            {
                throw new NexusApiException($"请求被限流，请在 {until:HH:mm} 后重试。");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
            request.Headers.Add("apikey", apiKey);
            request.Headers.Add("Application-Name", "LSMA");
            request.Headers.Add("Application-Version", GetVersion());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _client.SendAsync(request);
            ReadLimits(response);
            if (!response.IsSuccessStatusCode)
            {
                throw ToFriendlyException(response.StatusCode);
            }

            await using var content = await response.Content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync<T>(content, JsonHelper.Options)
                ?? throw new NexusApiException("Nexus 返回了无法读取的数据。");
        }
        catch (NexusApiException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync("Nexus API 请求失败", exception);
            throw new NexusApiException("无法连接 Nexus Mods，请检查网络后重试。");
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private void ReadLimits(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("x-rl-hourly-remaining", out var hourly)
            && int.TryParse(hourly.FirstOrDefault(), out var hourlyValue))
        {
            HourlyRequestsRemaining = hourlyValue;
        }

        if (response.Headers.TryGetValues("x-rl-daily-remaining", out var daily)
            && int.TryParse(daily.FirstOrDefault(), out var dailyValue))
        {
            DailyRequestsRemaining = dailyValue;
        }
    }

    private NexusApiException ToFriendlyException(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => new NexusApiException("授权码无效，请重新生成后保存。", statusCode),
            HttpStatusCode.Forbidden => new NexusApiException("当前账户需要浏览器确认下载，请在 Nexus 页面选择文件。", statusCode),
            HttpStatusCode.NotFound => new NexusApiException("Nexus 上找不到该资源。", statusCode),
            HttpStatusCode.TooManyRequests => StartCooldown(),
            _ => new NexusApiException($"Nexus 请求失败，服务返回 {(int)statusCode}。", statusCode)
        };
    }

    private NexusApiException StartCooldown()
    {
        _cooldownUntil = DateTime.Now.AddMinutes(5);
        return new NexusApiException("请求被限流，LSMA 已暂停在线请求 5 分钟。", HttpStatusCode.TooManyRequests);
    }

    private static string GetVersion()
        => typeof(NexusClient).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
}

public sealed class NexusApiException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
    public bool RequiresBrowserDownload => StatusCode == HttpStatusCode.Forbidden;
}
