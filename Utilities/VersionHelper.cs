namespace LSMA.Utilities;

public static class VersionHelper
{
    public static bool IsAtLeast(string? installed, string? required)
    {
        if (string.IsNullOrWhiteSpace(required))
        {
            return true;
        }

        if (TryNormalize(installed, out var installedVersion) && TryNormalize(required, out var requiredVersion))
        {
            return installedVersion >= requiredVersion;
        }

        return string.Equals(NormalizeText(installed), NormalizeText(required), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNormalize(string? value, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = NormalizeText(value);
        if (text.StartsWith('v') && text.Length > 1 && char.IsDigit(text[1]))
        {
            text = text[1..];
        }

        var metadataIndex = text.IndexOf('+');
        if (metadataIndex >= 0)
        {
            text = text[..metadataIndex];
        }

        if (!Version.TryParse(text, out var parsed))
        {
            return false;
        }

        version = new Version(
            parsed.Major,
            parsed.Minor < 0 ? 0 : parsed.Minor,
            parsed.Build < 0 ? 0 : parsed.Build,
            parsed.Revision < 0 ? 0 : parsed.Revision);
        return true;
    }

    private static string NormalizeText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}
