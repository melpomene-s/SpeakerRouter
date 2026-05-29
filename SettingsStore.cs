using System.Text.Json;

namespace SpeakerRouter;

internal sealed class AppSettings
{
    public bool RoutingEnabled { get; set; } = true;
    public int ScanIntervalMs { get; set; } = 800;
    public Dictionary<string, string> MonitorDeviceMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> AudioDeviceNameMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpeakerRouter");

    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
