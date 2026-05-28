using System.Text.Json;
using System.Text.Json.Serialization;

namespace LSMA.Utilities;

public static class JsonHelper
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken);
    }

    public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
            }

            File.Move(temporaryPath, path, true);
        }
        catch
        {
            // Clean up orphaned temp file on failure.
            try { File.Delete(temporaryPath); } catch { /* best effort */ }
            throw;
        }
    }
}
