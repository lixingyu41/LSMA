using System.Net;
using System.Net.Http.Headers;
using System.Text;
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

    public async Task<List<NexusCategory>> GetModCategoriesAsync()
    {
        var payload = new
        {
            query = """
                query ModCategories($filter: ModsFilter, $facets: ModsFacet) {
                  mods(filter: $filter, facets: $facets, count: 0) {
                    facets {
                      facet
                      value
                      count
                    }
                  }
                }
                """,
            variables = new
            {
                filter = CreateModsFilter(null, null),
                facets = new { categoryName = Array.Empty<string>() }
            }
        };

        var envelope = await PostGraphQlAsync<ModFacetsGraphQlData>(payload);
        var categories = envelope.Data?.Mods?.Facets
            .Where(facet => string.Equals(facet.Facet, "categoryName", StringComparison.OrdinalIgnoreCase))
            .Where(facet => !string.IsNullOrWhiteSpace(facet.Value))
            .Select((facet, index) => new NexusCategory { CategoryId = index + 1, Name = facet.Value })
            .OrderBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return categories is { Count: > 0 }
            ? categories
            : throw new NexusApiException("Nexus 未返回可用分类。");
    }

    public async Task<NexusModSearchResult> SearchModsAsync(
        string? query,
        string? categoryName,
        int offset,
        int count)
    {
        var filter = CreateModsFilter(query, categoryName);
        var sort = string.IsNullOrWhiteSpace(query)
            ? new object[] { new { updatedAt = new { direction = "DESC" } } }
            : [new { relevance = new { direction = "DESC" } }, new { downloads = new { direction = "DESC" } }];
        var payload = new
        {
            query = """
                query SearchMods($filter: ModsFilter, $sort: [ModsSort!], $offset: Int, $count: Int) {
                  mods(filter: $filter, sort: $sort, offset: $offset, count: $count) {
                    totalCount
                    nodes {
                      modId
                      name
                      summary
                      version
                      author
                      category
                      modCategory { categoryId }
                      updatedAt
                      endorsements
                      downloads
                    }
                  }
                }
                """,
            variables = new
            {
                filter,
                sort,
                offset = Math.Max(0, offset),
                count = Math.Clamp(count, 1, 50)
            }
        };

        var envelope = await PostGraphQlAsync<ModsGraphQlData>(payload);
        var page = envelope.Data?.Mods ?? throw new NexusApiException("Nexus 返回了无法读取的数据。");
        var mods = page.Nodes.Select(ToNexusModInfo).Where(mod => mod.ModId > 0).ToList();
        PrioritizeExactNameMatches(mods, query);
        return new NexusModSearchResult
        {
            Mods = mods,
            TotalCount = page.TotalCount
        };
    }

    private static void PrioritizeExactNameMatches(List<NexusModInfo> mods, string? query)
    {
        if (mods.Count <= 1 || string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        var normalizedQuery = NormalizeSearchName(query);
        var sorted = mods
            .Select((mod, index) => new
            {
                Mod = mod,
                Index = index,
                Exact = string.Equals(NormalizeSearchName(mod.Name), normalizedQuery, StringComparison.OrdinalIgnoreCase)
            })
            .OrderByDescending(item => item.Exact)
            .ThenBy(item => item.Index)
            .Select(item => item.Mod)
            .ToList();

        mods.Clear();
        mods.AddRange(sorted);
    }

    public async Task<IReadOnlyList<NexusFileInfo>> GetFilesAsync(long modId, string apiKey)
    {
        var value = await GetAsync<NexusFilesResponse>($"games/{GameDomain}/mods/{modId}/files.json", apiKey);
        return (value.Files ?? [])
            .OfType<NexusFileInfo>()
            .Where(file => file.FileId > 0)
            .OrderByDescending(file => string.Equals(file.CategoryName, "MAIN", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(file => file.UploadedTimestamp)
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

    private async Task<GraphQlEnvelope<T>> PostGraphQlAsync<T>(object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.nexusmods.com/v2/graphql");
        request.Headers.Add("Application-Name", "LSMA");
        request.Headers.Add("Application-Version", GetVersion());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonHelper.Options), Encoding.UTF8, "application/json");

        using var response = await _client.SendAsync(request);
        ReadLimits(response);
        if (!response.IsSuccessStatusCode)
        {
            throw ToFriendlyException(response.StatusCode);
        }

        await using var content = await response.Content.ReadAsStreamAsync();
        var envelope = await JsonSerializer.DeserializeAsync<GraphQlEnvelope<T>>(content, JsonHelper.Options)
            ?? throw new NexusApiException("Nexus 返回了无法读取的数据。");
        if (envelope.Errors is { Count: > 0 })
        {
            throw new NexusApiException(envelope.Errors[0].Message ?? "Nexus 请求失败。");
        }

        return envelope;
    }

    private static object CreateModsFilter(string? query, string? categoryName)
    {
        var filters = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["gameDomainName"] = new[] { new { value = GameDomain, op = "EQUALS" } },
                ["status"] = new[] { new { value = "published", op = "EQUALS" } }
            }
        };

        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            filters.Add(new Dictionary<string, object?>
            {
                ["categoryName"] = new[] { new { value = categoryName, op = "EQUALS" } }
            });
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var value = query.Trim();
            filters.Add(new Dictionary<string, object?>
            {
                ["op"] = "OR",
                ["filter"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = new[] { new { value, op = "WILDCARD" } }
                    },
                    new Dictionary<string, object?>
                    {
                        ["nameStemmed"] = new[] { new { value, op = "MATCHES" } }
                    },
                    new Dictionary<string, object?>
                    {
                        ["description"] = new[] { new { value, op = "MATCHES" } }
                    }
                }
            });
        }

        return new Dictionary<string, object?>
        {
            ["op"] = "AND",
            ["filter"] = filters
        };
    }

    private static NexusModInfo ToNexusModInfo(GraphQlModNode node)
    {
        return new NexusModInfo
        {
            ModId = node.ModId,
            Name = node.Name ?? string.Empty,
            Summary = node.Summary ?? string.Empty,
            Version = string.IsNullOrWhiteSpace(node.Version) ? "-" : node.Version,
            Author = string.IsNullOrWhiteSpace(node.Author) ? "未知作者" : node.Author,
            CategoryId = node.ModCategory?.CategoryId ?? 0,
            UpdatedTimestamp = ToUnixTimestamp(node.UpdatedAt),
            Endorsements = node.Endorsements,
            Downloads = node.Downloads
        };
    }

    private static long ToUnixTimestamp(string? value)
    {
        return DateTimeOffset.TryParse(value, out var timestamp)
            ? timestamp.ToUnixTimeSeconds()
            : 0;
    }

    private static string NormalizeSearchName(string value)
        => string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string GetVersion()
        => typeof(NexusClient).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    private sealed class GraphQlEnvelope<T>
    {
        public T? Data { get; set; }
        public List<GraphQlError>? Errors { get; set; }
    }

    private sealed class GraphQlError
    {
        public string? Message { get; set; }
    }

    private sealed class ModsGraphQlData
    {
        public ModsGraphQlPage? Mods { get; set; }
    }

    private sealed class ModFacetsGraphQlData
    {
        public ModFacetsGraphQlPage? Mods { get; set; }
    }

    private sealed class ModsGraphQlPage
    {
        public int TotalCount { get; set; }
        public List<GraphQlModNode> Nodes { get; set; } = [];
    }

    private sealed class ModFacetsGraphQlPage
    {
        public List<GraphQlFacetNode> Facets { get; set; } = [];
    }

    private sealed class GraphQlFacetNode
    {
        public string? Facet { get; set; }
        public string Value { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    private sealed class GraphQlModNode
    {
        public long ModId { get; set; }
        public string? Name { get; set; }
        public string? Summary { get; set; }
        public string? Version { get; set; }
        public string? Author { get; set; }
        public string? UpdatedAt { get; set; }
        public int Endorsements { get; set; }
        public long Downloads { get; set; }
        public GraphQlModCategory? ModCategory { get; set; }
    }

    private sealed class GraphQlModCategory
    {
        public int CategoryId { get; set; }
    }
}

public sealed class NexusApiException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
    public bool RequiresBrowserDownload => StatusCode == HttpStatusCode.Forbidden;
}
