namespace LSMA.Services;

public sealed record NexusDownloadToken(string GameDomain, long ModId, long FileId, string Key, long Expires)
{
    private const string ExpectedScheme = "nxm";

    public bool IsExpired => DateTimeOffset.FromUnixTimeSeconds(Expires) <= DateTimeOffset.UtcNow.AddSeconds(30);

    public static bool TryParse(string value, out NexusDownloadToken? token)
    {
        token = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, ExpectedScheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = uri.AbsolutePath
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 4
            || !string.Equals(parts[0], "mods", StringComparison.OrdinalIgnoreCase)
            || !long.TryParse(parts[1], out var modId)
            || !string.Equals(parts[2], "files", StringComparison.OrdinalIgnoreCase)
            || !long.TryParse(parts[3], out var fileId))
        {
            return false;
        }

        var query = ParseQuery(uri.Query);
        if (!query.TryGetValue("key", out var key)
            || string.IsNullOrWhiteSpace(key)
            || !query.TryGetValue("expires", out var expiresValue)
            || !long.TryParse(expiresValue, out var expires))
        {
            return false;
        }

        token = new NexusDownloadToken(uri.Host, modId, fileId, key, expires);
        return true;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.Split('=', 2);
            if (split.Length != 2)
            {
                continue;
            }

            values[Uri.UnescapeDataString(split[0])] = Uri.UnescapeDataString(split[1]);
        }

        return values;
    }
}

public sealed record NexusRequirementsPopupLink(long FileId, int GameId, Uri Uri)
{
    public static bool TryParse(string value, out NexusRequirementsPopupLink? link)
    {
        link = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, "www.nexusmods.com", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.Equals("/Core/Libs/Common/Widgets/ModRequirementsPopUp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var query = ParseQuery(uri.Query);
        if (!query.TryGetValue("id", out var fileIdValue)
            || !long.TryParse(fileIdValue, out var fileId)
            || !query.TryGetValue("game_id", out var gameIdValue)
            || !int.TryParse(gameIdValue, out var gameId))
        {
            return false;
        }

        link = new NexusRequirementsPopupLink(fileId, gameId, uri);
        return true;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.Split('=', 2);
            if (split.Length == 2)
            {
                values[Uri.UnescapeDataString(split[0])] = Uri.UnescapeDataString(split[1]);
            }
        }

        return values;
    }
}
