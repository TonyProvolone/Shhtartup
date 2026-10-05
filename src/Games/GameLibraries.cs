using System.Text.Json;
using Microsoft.Win32;

namespace Shhtartup;

// Recognises games by where they're installed, as a fallback for when the launcher process chain
// can't be traced (game started from its own exe or a desktop shortcut, or launched through a helper
// that exited before it could be seen). Install roots come from each launcher's own records, so
// custom install locations (e.g. D:\Games\...) are covered, not just the defaults.
internal static class GameLibraries
{
    // Folder names every install of these launchers uses, regardless of drive.
    private static readonly (string Fragment, string Source)[] FolderFragments =
    [
        (@"\steamapps\common\", "steam"),
        (@"\epic games\", "epic"),
        (@"\gog games\", "gog"),
        (@"\gog galaxy\games\", "gog"),
        (@"\ubisoft game launcher\games\", "ubisoft"),
        (@"\origin games\", "ea"),
        (@"\ea games\", "ea"),
    ];

    // The launchers' own program folders sit under some of the folders above.
    private static readonly string[] ExcludedPathFragments =
    [
        @"\epic games\launcher\",
        @"\epic games\epic online services\",
        @"\electronic arts\ea desktop\",
    ];

    // Uninstall entries for the launchers themselves (as opposed to their games).
    private static readonly string[] LauncherDisplayNames =
    [
        "Battle.net", "EA app", "EA", "Origin", "Ubisoft Connect", "Epic Games Launcher", "GOG GALAXY", "Steam",
    ];

    private static readonly string WindowsDir = WithTrailingSlash(Environment.GetFolderPath(Environment.SpecialFolder.Windows));

    private static IReadOnlyList<(string Root, string Source)> _roots = [];

    public static void Refresh()
    {
        var roots = new List<(string Root, string Source)>();

        foreach (var dir in EpicInstallDirs())
        {
            AddRoot(roots, dir, "epic");
        }
        foreach (var dir in RegistrySubkeyValues(@"SOFTWARE\GOG.com\Games", "path"))
        {
            AddRoot(roots, dir, "gog");
        }
        foreach (var dir in RegistrySubkeyValues(@"SOFTWARE\Ubisoft\Launcher\Installs", "InstallDir"))
        {
            AddRoot(roots, dir, "ubisoft");
        }
        foreach (var (dir, source) in PublisherInstallDirs())
        {
            AddRoot(roots, dir, source);
        }

        _roots = roots;
    }

    // Returns which library a path belongs to, or null if it isn't a recognised game location.
    public static string? Classify(string path)
    {
        if (IsSystemPath(path))
        {
            return null;
        }

        foreach (var fragment in ExcludedPathFragments)
        {
            if (path.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        foreach (var (root, source) in _roots)
        {
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return source;
            }
        }

        foreach (var (fragment, source) in FolderFragments)
        {
            if (path.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return source;
            }
        }

        return null;
    }

    public static bool IsSystemPath(string path) => path.StartsWith(WindowsDir, StringComparison.OrdinalIgnoreCase);

    // Epic keeps one JSON manifest per installed game.
    private static IEnumerable<string> EpicInstallDirs()
    {
        var manifests = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

        string[] files;
        try
        {
            files = Directory.GetFiles(manifests, "*.item");
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
        {
            string? dir = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.TryGetProperty("InstallLocation", out var location))
                {
                    dir = location.GetString();
                }
            }
            catch
            {
                // Unreadable or malformed manifest -- skip it.
            }

            if (dir is not null)
            {
                yield return dir;
            }
        }
    }

    // GOG and Ubisoft keep one registry subkey per installed game (32-bit view, under WOW6432Node).
    private static IEnumerable<string> RegistrySubkeyValues(string keyPath, string valueName)
    {
        var results = new List<string>();
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = hklm.OpenSubKey(keyPath);
                if (key is null)
                {
                    continue;
                }

                foreach (var name in key.GetSubKeyNames())
                {
                    using var sub = key.OpenSubKey(name);
                    if (sub?.GetValue(valueName) is string value && value.Length > 0)
                    {
                        results.Add(value);
                    }
                }
            }
            catch
            {
                // Missing or inaccessible key -- that launcher just isn't installed.
            }
        }
        return results;
    }

    // Battle.net and EA games register normal Windows uninstall entries with their publisher name.
    private static IEnumerable<(string Dir, string Source)> PublisherInstallDirs()
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

                foreach (var name in key.GetSubKeyNames())
                {
                    using var sub = key.OpenSubKey(name);
                    if (sub is null)
                    {
                        continue;
                    }

                    var publisher = sub.GetValue("Publisher") as string ?? string.Empty;
                    var source = publisher.Contains("Blizzard", StringComparison.OrdinalIgnoreCase) ? "battle.net"
                        : publisher.Contains("Electronic Arts", StringComparison.OrdinalIgnoreCase) ? "ea"
                        : null;
                    if (source is null)
                    {
                        continue;
                    }

                    var displayName = sub.GetValue("DisplayName") as string ?? string.Empty;
                    if (LauncherDisplayNames.Any(n => displayName.Equals(n, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    if (sub.GetValue("InstallLocation") is string dir && dir.Length > 0)
                    {
                        results.Add((dir, source));
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

    private static void AddRoot(List<(string Root, string Source)> roots, string rawDir, string source)
    {
        string root;
        try
        {
            root = WithTrailingSlash(Path.GetFullPath(rawDir.Trim().Trim('"')));
        }
        catch
        {
            return;
        }

        // A bad registry entry pointing at e.g. C:\ or Program Files would turn every app into a "game".
        if (IsTooBroad(root) || roots.Any(r => r.Root.Equals(root, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        roots.Add((root, source));
    }

    public static bool IsTooBroad(string root)
    {
        if (root.Length <= 3)
        {
            return true;
        }

        var broad = new[]
        {
            Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.CommonApplicationData,
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.LocalApplicationData,
        };

        return broad.Any(folder =>
        {
            var path = Environment.GetFolderPath(folder);
            return path.Length > 0 && WithTrailingSlash(path).Equals(root, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string WithTrailingSlash(string path) =>
        path.EndsWith('\\') ? path : path + '\\';
}
