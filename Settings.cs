using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shhtartup;

internal sealed class AppSettings
{
    public int DefaultVolumePercent { get; set; } = 25;
    public bool StartWithWindows { get; set; } = true;
}

[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext
{
}

internal static class Settings
{
    public static AppSettings Current { get; private set; } = new();

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Shhtartup", "settings.json");

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);
                if (loaded is not null)
                {
                    Current = loaded;
                    return;
                }
            }
        }
        catch
        {
            // Corrupt or unreadable settings file -- fall back to defaults below.
        }

        // First run (or corrupt settings file): apply defaults and make "Run on startup" actually
        // take effect immediately, rather than just being an unsaved in-memory default.
        Current = new AppSettings();
        AutoStart.SetEnabled(Current.StartWithWindows);
        Save();
    }

    // Shared entry point for changing the default volume from any UI (settings window or tray
    // flyout): clamps, and persists on change unless save is false (e.g. mid-drag, saved on release).
    // Returns the clamped value actually stored.
    public static int SetDefaultVolume(int value, bool save = true)
    {
        var clamped = Math.Clamp(value, 0, 100);
        if (Current.DefaultVolumePercent != clamped)
        {
            Current.DefaultVolumePercent = clamped;
            if (save)
            {
                Save();
            }
        }
        return clamped;
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var json = JsonSerializer.Serialize(Current, SettingsJsonContext.Default.AppSettings);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Best-effort persistence -- a failed save shouldn't crash a tray-only background utility.
        }
    }
}
