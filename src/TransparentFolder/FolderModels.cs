using System.Globalization;
using System.Text.Json.Serialization;

namespace TransparentFolder;

public sealed class AppState
{
    [JsonPropertyName("autoStart")]
    public bool AutoStart { get; set; } = true;

    [JsonPropertyName("hoverDelayMs")]
    public int HoverDelayMs { get; set; } = 400;

    [JsonPropertyName("animationDurationMs")]
    public int AnimationDurationMs { get; set; } = 400;

    [JsonPropertyName("glassOpacity")]
    public int GlassOpacity { get; set; } = 90;

    [JsonPropertyName("folders")]
    public List<FolderData> Folders { get; set; } = new();
}

public sealed class FolderData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    [JsonPropertyName("name")]
    public string Name { get; set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase)
        ? "Yeni Klasör"
        : "New Folder";

    [JsonPropertyName("storagePath")]
    public string StoragePath { get; set; } = string.Empty;

    [JsonPropertyName("storageDirName")]
    public string StorageDirName { get; set; } = string.Empty;

    [JsonPropertyName("customIconFile")]
    public string? CustomIconFile { get; set; }

    [JsonPropertyName("x")]
    public int X { get; set; } = 120;

    [JsonPropertyName("y")]
    public int Y { get; set; } = 120;

    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = "#a9f1fe";

    [JsonPropertyName("previewGrid")]
    public int PreviewGrid { get; set; } = 2; // 2 (2x2) or 3 (3x3)

    [JsonPropertyName("itemOrder")]
    public List<string> ItemOrder { get; set; } = new();
}

public sealed class FolderItemDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty; // file/dir name inside vault folder

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty; // display name without .lnk/.url

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("extension")]
    public string Extension { get; set; } = string.Empty;

    [JsonPropertyName("isDirectory")]
    public bool IsDirectory { get; set; }

    [JsonPropertyName("iconUrl")]
    public string IconUrl { get; set; } = string.Empty;
}
