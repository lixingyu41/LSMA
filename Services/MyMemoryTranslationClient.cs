using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using LSMA.Utilities;

namespace LSMA.Services;

public sealed class MyMemoryTranslationClient
{
    private static readonly Uri Endpoint = new("https://api.mymemory.translated.net/get");
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<string> TranslateSegmentAsync(string text, CancellationToken cancellationToken = default)
    {
        var requestUri = $"{Endpoint}?q={Uri.EscapeDataString(text)}&langpair=en%7Czh-CN&mt=1";
        using var response = await _http.GetAsync(requestUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"MyMemory 返回 HTTP {(int)response.StatusCode}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<MyMemoryGetResponse>(stream, JsonHelper.Options, cancellationToken)
            ?? throw new InvalidOperationException("MyMemory 返回内容为空");
        if (payload.ResponseStatus is { } status && status != 200)
        {
            throw new InvalidOperationException(payload.ResponseDetails ?? $"MyMemory 返回状态 {status}");
        }

        return WebUtility.HtmlDecode(payload.ResponseData?.TranslatedText ?? string.Empty).Trim();
    }

    private sealed class MyMemoryGetResponse
    {
        [JsonPropertyName("responseData")]
        public MyMemoryResponseData? ResponseData { get; set; }

        [JsonPropertyName("responseStatus")]
        public int? ResponseStatus { get; set; }

        [JsonPropertyName("responseDetails")]
        public string? ResponseDetails { get; set; }
    }

    private sealed class MyMemoryResponseData
    {
        [JsonPropertyName("translatedText")]
        public string? TranslatedText { get; set; }
    }
}
