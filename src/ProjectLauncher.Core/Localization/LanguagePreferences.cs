using System.Text.Json;

namespace ProjectLauncher.Core.Localization;

public sealed class LanguagePreferences(string path)
{
    private const int MaxBytes = 65536;
    public (string? Language, string? Error) Load()
    {
        try
        {
            if (!File.Exists(path)) return (null, null);
            if (new FileInfo(path).Length > MaxBytes) return (null, "Language preference file exceeds 64 KiB.");
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            if (json.RootElement.ValueKind != JsonValueKind.Object) return (null, "Invalid language preference object.");
            if (!json.RootElement.TryGetProperty("language", out var item)) return (null, null);
            var value = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            return value is "zh-CN" or "en-US" ? (value, null) : (null, "Unsupported language preference.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return (null, ex.Message); }
    }
    public string? Save(string language)
    {
        if (language is not ("zh-CN" or "en-US")) return "Unsupported language preference.";
        string? temporary = null;
        try
        {
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            if (File.Exists(fullPath) && Load().Error is not null)
                File.Copy(fullPath, fullPath + ".corrupt-" + Guid.NewGuid().ToString("N"), false);
            temporary = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { language }));
            File.Move(temporary, fullPath, true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return ex.Message; }
        finally
        {
            if (temporary is not null)
                try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
