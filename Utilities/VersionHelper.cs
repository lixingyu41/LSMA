namespace LSMA.Utilities;

public static class VersionHelper
{
    public static bool IsAtLeast(string? installed, string? required)
    {
        if (string.IsNullOrWhiteSpace(required))
        {
            return true;
        }

        return Version.TryParse(installed, out var installedVersion)
            && Version.TryParse(required, out var requiredVersion)
            && installedVersion >= requiredVersion;
    }
}
