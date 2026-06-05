using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using LSMA.Utilities;

namespace LSMA.Services;

public sealed class AppUpdateService(LoggingService logging)
{
    public const string ManifestUrl = "https://lsma.lixingyu.top/download/manifest.json";
    public const string FallbackDownloadPageUrl = "https://lsma.lixingyu.top/download/";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    public async Task<AppUpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ManifestUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            request.Headers.UserAgent.ParseAdd($"LSMA/{GetCurrentVersion()}");

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var manifest = await JsonSerializer.DeserializeAsync<AppUpdateManifest>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("更新清单为空。");

            var latestVersion = string.IsNullOrWhiteSpace(manifest.LatestVersion)
                ? manifest.Version
                : manifest.LatestVersion;
            if (string.IsNullOrWhiteSpace(latestVersion))
            {
                throw new InvalidOperationException("更新清单缺少 latestVersion。");
            }

            var currentVersion = GetCurrentVersion();
            var downloadPageUrl = string.IsNullOrWhiteSpace(manifest.DownloadPageUrl)
                ? FallbackDownloadPageUrl
                : manifest.DownloadPageUrl!;
            return new AppUpdateCheckResult(
                CurrentVersion: currentVersion,
                LatestVersion: latestVersion.Trim(),
                IsUpdateAvailable: !VersionHelper.IsAtLeast(currentVersion, latestVersion),
                DownloadPageUrl: downloadPageUrl,
                DownloadUrl: manifest.DownloadUrl ?? string.Empty,
                ReleaseDate: manifest.ReleaseDate ?? string.Empty,
                Notes: manifest.Notes ?? []);
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync("检查 LSMA 更新失败", exception);
            throw;
        }
    }

    public static string GetCurrentVersion()
    {
        var version = typeof(AppUpdateService).Assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }
}

public sealed record AppUpdateCheckResult(
    string CurrentVersion,
    string LatestVersion,
    bool IsUpdateAvailable,
    string DownloadPageUrl,
    string DownloadUrl,
    string ReleaseDate,
    IReadOnlyList<string> Notes);

public sealed class AppUpdateManifest
{
    [JsonPropertyName("latestVersion")]
    public string? LatestVersion { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("downloadPageUrl")]
    public string? DownloadPageUrl { get; set; }

    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; set; }

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("notes")]
    public List<string>? Notes { get; set; }
}
