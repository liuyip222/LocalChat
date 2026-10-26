using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace LocalChat;

public sealed class Settings
{
    public static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalChat", "settings.json");
    public string DataDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalChat", "Data");
    public string ExportDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "LocalChat");
    public bool EnterToSend { get; set; } = true;
    public int DefaultWindowWidth { get; set; } = 1080;
    public int DefaultWindowHeight { get; set; } = 800;
    public List<string> PreviousDataDirectories { get; set; } = [];
    public static Settings Load() => File.Exists(ConfigPath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(ConfigPath)) ?? new() : new();
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        var temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, ConfigPath, true);
    }
}

public sealed class Attachment
{
    public string Id { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public long Size { get; set; }
    public string Kind { get; set; } = "file";
    public string Hash { get; set; } = "";
    public string FullPath { get; set; } = "";
    public BitmapSource? Thumbnail { get; set; }
    public Visibility ImageVisibility => Thumbnail == null ? Visibility.Collapsed : Visibility.Visible;
    public string Icon => Kind == "video" ? "▶" : Kind == "image" ? "▧" : "▤";
    public string Detail => $"{(Kind == "video" ? "视频" : Kind == "image" ? "图片" : "文件")} · {FormatSize(Size)}";
    public static string FormatSize(long value) => value >= 1073741824 ? $"{value / 1073741824d:0.##} GB" : value >= 1048576 ? $"{value / 1048576d:0.##} MB" : value >= 1024 ? $"{value / 1024d:0.#} KB" : $"{value} B";
}

public sealed class Message
{
    public long Id { get; set; }
    public string Text { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public List<Attachment> Attachments { get; set; } = [];
    public string Timestamp => CreatedUtc.ToLocalTime().ToString("yyyy年MM月dd日  HH:mm:ss");
    public Visibility TextVisibility => string.IsNullOrEmpty(Text) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility TextActionsVisibility => string.IsNullOrWhiteSpace(Text) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DateVisibility { get; set; } = Visibility.Visible;
    public string DateLabel => CreatedUtc.ToLocalTime().ToString("yyyy年MM月dd日 dddd");
}

public sealed record PendingFile(string Path, string Name, bool Temporary = false);

public sealed record SearchFilter(string Keyword = "", DateTime? From = null, DateTime? Until = null, string Kind = "all");
