using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shhtartup;

internal sealed class KnownGame
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty; // exe file name
    // The game's real name (see GameNames), looked up once. Missing from files written before
    // names were looked up; Load() fills it in.
    public string? DisplayName { get; set; }

    [JsonIgnore]
    public string Title => DisplayName ?? System.IO.Path.GetFileNameWithoutExtension(Path);
    public string Source { get; set; } = string.Empty;
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    // Turn this one down on every launch, not just the first. Missing from files written before
    // this setting existed; Load() fills it in from the global setting.
    public bool? AdjustEveryLaunch { get; set; }
}

internal sealed class KnownGamesFile
{
    public List<KnownGame> Games { get; set; } = [];
}

[JsonSerializable(typeof(KnownGamesFile))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class KnownGamesJsonContext : JsonSerializerContext
{
}

// Every exe whose volume has been clamped is remembered by full path, so later launches are caught
// no matter how they're started (direct exe, desktop shortcut, launcher not running, etc.).
//
// Each one also has its own "adjust on every launch" choice. The global setting of the same name
// always equals "every remembered game has it on": changing one game updates the global setting,
// and changing the global setting changes every game. New games take the global setting's value,
// which keeps the two in step.
internal static class KnownGames
{
    private static readonly Dictionary<string, KnownGame> _byPath = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string FilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Shhtartup", "known-games.json");

    public static int Count => _byPath.Count;

    public static bool Contains(string path) => _byPath.ContainsKey(path);

    // Remembered and set to only be adjusted on its first launch.
    public static bool IsFirstLaunchOnly(string path) =>
        _byPath.TryGetValue(path, out var game) && game.AdjustEveryLaunch == false;

    // For display, in the same order as the saved file.
    public static List<KnownGame> Sorted() =>
        _byPath.Values
            .OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static void SetAdjustEveryLaunch(string path, bool on)
    {
        if (!_byPath.TryGetValue(path, out var game) || game.AdjustEveryLaunch == on)
        {
            return;
        }

        game.AdjustEveryLaunch = on;
        Save();
        SyncGlobalSetting();
    }

    // The global toggle: applies to every remembered game.
    public static void SetAdjustEveryLaunchForAll(bool on)
    {
        foreach (var game in _byPath.Values)
        {
            game.AdjustEveryLaunch = on;
        }
        Save();

        Settings.Current.AdjustEveryLaunch = on;
        Settings.Save();
    }

    private static void SyncGlobalSetting()
    {
        if (_byPath.Count == 0)
        {
            return; // Nothing to follow: the global setting keeps its own value.
        }

        var all = _byPath.Values.All(g => g.AdjustEveryLaunch == true);
        if (Settings.Current.AdjustEveryLaunch != all)
        {
            Settings.Current.AdjustEveryLaunch = all;
            Settings.Save();
        }
    }

    public static void Load()
    {
        _byPath.Clear();
        try
        {
            if (!File.Exists(FilePath))
            {
                return;
            }

            var file = JsonSerializer.Deserialize(File.ReadAllText(FilePath), KnownGamesJsonContext.Default.KnownGamesFile);
            var changed = false;
            foreach (var game in file?.Games ?? [])
            {
                // Helpers that were mistaken for games before they were added to the exclusions.
                var excluded = LauncherCatalog.IsLauncherOrExcluded(System.IO.Path.GetFileName(game.Path));
                changed |= excluded;
                if (game.Path.Length > 0 && !excluded)
                {
                    // Lists from before per-game choices existed follow the global setting.
                    game.AdjustEveryLaunch ??= Settings.Current.AdjustEveryLaunch;
                    _byPath[game.Path] = game;
                }
            }

            // Lists from before names were looked up: look them all up once (one shared index).
            var unnamed = _byPath.Values.Where(g => g.DisplayName is null).ToList();
            if (unnamed.Count > 0)
            {
                var names = new GameNames.Index();
                foreach (var game in unnamed)
                {
                    game.DisplayName = LookUpName(game.Path, names);
                }
                changed = true;
            }

            if (changed)
            {
                Save();
            }

            SyncGlobalSetting();
        }
        catch
        {
            // Corrupt file (e.g. a bad hand edit): keep a copy so the next save doesn't silently destroy
            // the user's list, then start empty -- it rebuilds itself as games are seen.
            _byPath.Clear();
            try
            {
                File.Copy(FilePath, FilePath + ".bak", overwrite: true);
            }
            catch
            {
            }
        }
    }

    public static void Record(string path, string name, string source)
    {
        var now = DateTime.Now;
        if (_byPath.TryGetValue(path, out var existing))
        {
            existing.LastSeen = now;
        }
        else
        {
            _byPath[path] = new KnownGame
            {
                Path = path, Name = name, Source = source, FirstSeen = now, LastSeen = now,
                DisplayName = LookUpName(path, new GameNames.Index()),
                AdjustEveryLaunch = Settings.Current.AdjustEveryLaunch,
            };
        }
        Save();
        SettingsWindow.OnKnownGamesChanged();
    }

    // Falls back to the exe's name, which is also stored so the lookup isn't repeated every start.
    private static string LookUpName(string path, GameNames.Index names) =>
        names.Resolve(path) ?? System.IO.Path.GetFileNameWithoutExtension(path);

    public static void Clear()
    {
        _byPath.Clear();
        Save();
        SettingsWindow.OnKnownGamesChanged();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            var file = new KnownGamesFile { Games = Sorted() };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(file, KnownGamesJsonContext.Default.KnownGamesFile));
        }
        catch
        {
            // Best-effort persistence.
        }
    }
}
