using System.Text.RegularExpressions;
using LSMA.Models;
using Microsoft.Win32;

namespace LSMA.Services;

public sealed partial class GameLocatorService(
    AppStateService state,
    SettingsService settings,
    LoggingService logging)
{
    public static bool IsValidDirectory(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && Directory.Exists(path)
            && (File.Exists(Path.Combine(path, "Stardew Valley.exe"))
                || File.Exists(Path.Combine(path, "StardewModdingAPI.exe")));
    }

    public async Task DetectAsync()
    {
        foreach (var candidate in GetAutomaticCandidates())
        {
            if (await ConfigureAsync(candidate))
            {
                await logging.InfoAsync("已自动检测到游戏目录");
                return;
            }
        }

        if (await ConfigureAsync(settings.Current.GameDirectory))
        {
            return;
        }

        state.GameDirectory = null;
    }

    public async Task<bool> ConfigureAsync(string? path)
    {
        if (!IsValidDirectory(path))
        {
            return false;
        }

        var normalized = Path.GetFullPath(path!);
        state.GameDirectory = new GameDirectoryState(
            normalized,
            File.Exists(Path.Combine(normalized, "StardewModdingAPI.exe")),
            File.Exists(Path.Combine(normalized, "Stardew Valley.exe")));
        await settings.UpdateAsync(value => value.GameDirectory = normalized);
        return true;
    }

    private IEnumerable<string> GetAutomaticCandidates()
    {
        yield return @"C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley";

        foreach (var steamRoot in GetSteamRoots())
        {
            var libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFile))
            {
                continue;
            }

            string content;
            try
            {
                content = File.ReadAllText(libraryFile);
            }
            catch
            {
                continue;
            }

            foreach (Match match in SteamPathRegex().Matches(content))
            {
                var library = match.Groups[1].Value.Replace(@"\\", @"\");
                var manifest = Path.Combine(library, "steamapps", "appmanifest_413150.acf");
                var gameFolder = Path.Combine(library, "steamapps", "common", "Stardew Valley");
                if (File.Exists(manifest) || IsValidDirectory(gameFolder))
                {
                    yield return gameFolder;
                }
            }
        }

        yield return @"C:\Program Files (x86)\GOG Galaxy\Games\Stardew Valley";
    }

    private static IEnumerable<string> GetSteamRoots()
    {
        yield return @"C:\Program Files (x86)\Steam";

        var registryLocations = new[]
        {
            (RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
            (RegistryHive.LocalMachine, @"Software\WOW6432Node\Valve\Steam", "InstallPath")
        };

        foreach (var (hive, keyPath, valueName) in registryLocations)
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Default).OpenSubKey(keyPath);
            if (key?.GetValue(valueName) is string path && Directory.Exists(path))
            {
                yield return path.Replace('/', '\\');
            }
        }
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex SteamPathRegex();
}
