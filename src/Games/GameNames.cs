using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Shhtartup;

// Finds a game's or app's real name (e.g. "ELDEN RING" rather than "eldenring.exe") from, in order:
//   1. Steam's app manifest for the library folder it's in
//   2. Epic's install manifests
//   3. Windows' installed-programs list (Settings > Apps), which Steam, GOG, Ubisoft, EA, Battle.net
//      and most ordinary installers fill in
//   4. The exe's own description, unless it's a generic engine name
//   5. The install folder inside a launcher library (e.g. steamapps\common\Vampire Survivors)
// Lookups read the registry and launcher files, so a batch of lookups shares one Index.
internal static class GameNames
{
    // Engine/launcher stub descriptions that say nothing about the game.
    private static readonly string[] GenericDescriptions =
    [
        "BootstrapPackagedGame", "UE4Game", "UE5Game", "UnrealGame", "Unreal Engine", "Unity", "UnityPlayer",
        "Game", "Launcher", "Application", "Main", "Client",
    ];

    internal sealed class Index
    {
        // Installed programs: install folder (with trailing slash) and display name. Built on first use.
        private List<(string Root, string Name)>? _installed;
        private List<(string Root, string Name)>? _epic;

        public string? Resolve(string exePath) =>
            Try(() => SteamName(exePath))
            ?? Try(() => LongestMatch(_epic ??= EpicGames(), exePath))
            ?? Try(() => LongestMatch(_installed ??= InstalledPrograms(), exePath))
            ?? Try(() => DescriptionName(exePath))
            ?? Try(() => LibraryFolderName(exePath));
    }

    // Launcher library folders whose next folder down is one game's install folder.
    private static readonly string[] LibraryFolders =
    [
        @"\steamapps\common\", @"\epic games\", @"\gog games\", @"\gog galaxy\games\",
        @"\ubisoft game launcher\games\", @"\origin games\", @"\ea games\",
    ];

    // Last resort for library games: the install folder is usually named after the game (and
    // still works once the game is uninstalled and its launcher's record is gone).
    private static string? LibraryFolderName(string exePath)
    {
        foreach (var library in LibraryFolders)
        {
            var at = exePath.IndexOf(library, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                continue;
            }

            var rest = exePath[(at + library.Length)..];
            var slash = rest.IndexOf('\\');
            return slash > 0 ? Clean(rest[..slash]) : null;
        }
        return null;
    }

    // One source failing (missing folder, unreadable file) just moves on to the next.
    private static string? Try(Func<string?> source)
    {
        try
        {
            return source();
        }
        catch
        {
            return null;
        }
    }

    // Steam games live in <library>\steamapps\common\<installdir>\, and that library's
    // steamapps\appmanifest_<id>.acf names the game whose "installdir" matches.
    private static string? SteamName(string exePath)
    {
        const string marker = @"\steamapps\common\";
        var at = exePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return null;
        }

        var rest = exePath[(at + marker.Length)..];
        var slash = rest.IndexOf('\\');
        if (slash <= 0)
        {
            return null;
        }

        var installDir = rest[..slash];
        var steamapps = exePath[..(at + @"\steamapps".Length)];
        foreach (var file in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
        {
            var text = File.ReadAllText(file);
            var dir = AcfValue(text, "installdir");
            if (dir is not null && dir.Equals(installDir, StringComparison.OrdinalIgnoreCase))
            {
                return Clean(AcfValue(text, "name"));
            }
        }
        return null;
    }

    private static string? AcfValue(string acf, string key)
    {
        var match = Regex.Match(acf, $"\"{key}\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"") : null;
    }

    private static List<(string Root, string Name)> EpicGames()
    {
        var results = new List<(string, string)>();
        var manifests = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests))
        {
            return results;
        }

        foreach (var file in Directory.EnumerateFiles(manifests, "*.item"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.TryGetProperty("InstallLocation", out var location) &&
                    doc.RootElement.TryGetProperty("DisplayName", out var name))
                {
                    AddRoot(results, location.GetString(), name.GetString());
                }
            }
            catch
            {
                // Unreadable or malformed manifest -- skip it.
            }
        }
        return results;
    }

    private static List<(string Root, string Name)> InstalledPrograms()
    {
        const string uninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        var results = new List<(string, string)>();
        var hives = new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Default),
        };

        foreach (var (hive, view) in hives)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(uninstallPath);
                if (key is null)
                {
                    continue;
                }

                foreach (var subName in key.GetSubKeyNames())
                {
                    using var sub = key.OpenSubKey(subName);
                    if (sub?.GetValue("DisplayName") is not string name)
                    {
                        continue;
                    }

                    if (sub.GetValue("InstallLocation") is string location && location.Length > 0)
                    {
                        AddRoot(results, location, name);
                    }
                    else if (sub.GetValue("DisplayIcon") is string icon && icon.Length > 0)
                    {
                        // Often the program's own exe, e.g. "C:\Games\Foo\foo.exe",0.
                        var iconPath = icon.Split(',')[0].Trim().Trim('"');
                        AddRoot(results, Path.GetDirectoryName(iconPath), name);
                    }
                }
            }
            catch
            {
                // Inaccessible hive -- skip it.
            }
        }
        return results;
    }

    private static void AddRoot(List<(string Root, string Name)> results, string? dir, string? name)
    {
        name = Clean(name);
        if (string.IsNullOrWhiteSpace(dir) || name is null)
        {
            return;
        }

        try
        {
            var full = Path.GetFullPath(dir.Trim().Trim('"'));
            var root = full.EndsWith('\\') ? full : full + '\\';
            // A program "installed" to C:\ or Program Files would claim every exe under it.
            if (!GameLibraries.IsTooBroad(root))
            {
                results.Add((root, name));
            }
        }
        catch
        {
            // Not a valid path.
        }
    }

    // The most specific (longest) install folder containing the exe.
    private static string? LongestMatch(List<(string Root, string Name)> roots, string exePath)
    {
        string? best = null;
        var bestLength = 0;
        foreach (var (root, name) in roots)
        {
            if (root.Length > bestLength && exePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                best = name;
                bestLength = root.Length;
            }
        }
        return best;
    }

    private static string? DescriptionName(string exePath)
    {
        var info = FileVersionInfo.GetVersionInfo(exePath);
        var exeName = Path.GetFileNameWithoutExtension(exePath);
        foreach (var candidate in new[] { info.FileDescription, info.ProductName })
        {
            var name = Clean(candidate);
            if (name is null || name.Equals(exeName, StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Shipping", StringComparison.OrdinalIgnoreCase) ||
                GenericDescriptions.Any(g => name.Equals(g, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            return name;
        }
        return null;
    }

    // Trims and drops trademark symbols, which launchers often include ("Game™").
    private static string? Clean(string? name)
    {
        if (name is null)
        {
            return null;
        }

        name = name.Replace("™", "").Replace("®", "").Replace("©", "").Trim();
        return name.Length == 0 ? null : name;
    }
}
