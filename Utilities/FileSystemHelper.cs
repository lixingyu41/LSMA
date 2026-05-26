namespace LSMA.Utilities;

public static class FileSystemHelper
{
    public static string SafeFilePart(string? value)
    {
        var input = string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
        var invalid = Path.GetInvalidFileNameChars();
        return new string(input.Where(c => !invalid.Contains(c)).ToArray()).Replace(' ', '_');
    }
}
