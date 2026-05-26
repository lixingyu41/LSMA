using LSMA.Models;
using LSMA.Utilities;

namespace LSMA.Services;

public sealed class ModAnalyzerService(SettingsService settings)
{
    public IReadOnlyList<ModInfo> Analyze(List<ModInfo> mods)
    {
        var enabledById = mods
            .Where(mod => mod.IsEnabled && !string.IsNullOrWhiteSpace(mod.Manifest?.UniqueID))
            .GroupBy(mod => mod.Manifest!.UniqueID!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var mod in mods.Where(mod => mod.Manifest is not null))
        {
            var manifest = mod.Manifest!;
            mod.NexusModId = GetNexusModId(manifest.UpdateKeys)
                ?? (manifest.UniqueID is not null && settings.Current.NexusBindings is not null && settings.Current.NexusBindings.TryGetValue(manifest.UniqueID, out var binding)
                    ? binding
                    : null);
            if (string.IsNullOrWhiteSpace(manifest.UniqueID))
            {
                AddError(mod, "缺少模组识别名");
            }

            if (mod.IsEnabled && !string.IsNullOrWhiteSpace(manifest.EntryDll)
                && !File.Exists(Path.Combine(mod.FolderPath, manifest.EntryDll)))
            {
                AddError(mod, "模组程序文件缺失");
            }

            if (!mod.IsEnabled)
            {
                continue;
            }

            foreach (var dependency in (manifest.Dependencies ?? []).Where(value => value.IsRequired))
            {
                if (string.IsNullOrWhiteSpace(dependency.UniqueID)
                    || !enabledById.TryGetValue(dependency.UniqueID, out var installed))
                {
                    AddError(mod, $"缺少前置模组：{dependency.UniqueID ?? "未指定"}");
                    continue;
                }

                if (!VersionHelper.IsAtLeast(installed[0].Manifest?.Version, dependency.MinimumVersion))
                {
                    AddError(mod, $"前置版本不足：{dependency.UniqueID}");
                }
            }

            if (manifest.ContentPackFor?.UniqueID is { Length: > 0 } parentId
                && !enabledById.ContainsKey(parentId))
            {
                AddError(mod, $"内容包对应主模组缺失：{parentId}");
            }
        }

        foreach (var group in enabledById.Values.Where(values => values.Count > 1))
        {
            foreach (var mod in group)
            {
                AddError(mod, "重复模组识别名");
            }
        }

        return mods;
    }

    private static long? GetNexusModId(IEnumerable<string>? updateKeys)
    {
        foreach (var key in updateKeys ?? [])
        {
            if (key.StartsWith("Nexus:", StringComparison.OrdinalIgnoreCase)
                && long.TryParse(key["Nexus:".Length..], out var id))
            {
                return id;
            }
        }

        return null;
    }

    private static void AddError(ModInfo mod, string message)
    {
        if (!mod.Issues.Any(issue => issue.Message == message))
        {
            mod.Issues.Add(new ModIssue { Severity = IssueSeverity.Error, Message = message });
        }
    }
}
