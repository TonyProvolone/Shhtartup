using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tinnitdown;

internal sealed class KnownGame
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
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
internal static class KnownGames
{
    private static readonly Dictionary<string, KnownGame> _byPath = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string FilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Tinnitdown", "known-games.json");

    public static int Count => _byPath.Count;

    public static bool Contains(string path) => _byPath.ContainsKey(path);

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
            foreach (var game in file?.Games ?? [])
            {
                if (game.Path.Length > 0)
                {
                    _byPath[game.Path] = game;
                }
            }
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
            _byPath[path] = new KnownGame { Path = path, Name = name, Source = source, FirstSeen = now, LastSeen = now };
        }
        Save();
    }

    public static void Clear()
    {
        _byPath.Clear();
        Save();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            var file = new KnownGamesFile { Games = _byPath.Values.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList() };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(file, KnownGamesJsonContext.Default.KnownGamesFile));
        }
        catch
        {
            // Best-effort persistence.
        }
    }
}
