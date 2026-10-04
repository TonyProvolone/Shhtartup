namespace Tinnitdown;

// Carries settings over from the app's previous name (VolumeGuard): its AppData folder (settings and
// remembered games) and its "Run on startup" entry. Runs on every launch; does nothing once migrated.
internal static class LegacyMigration
{
    private const string LegacyName = "VolumeGuard";

    public static void Run()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var legacyDir = Path.Combine(appData, LegacyName);
            var currentDir = Path.Combine(appData, "Tinnitdown");
            if (Directory.Exists(legacyDir) && !Directory.Exists(currentDir))
            {
                Directory.Move(legacyDir, currentDir);
            }
        }
        catch
        {
            // Couldn't move it (e.g. a file is open) -- the app simply starts with defaults.
        }

        try
        {
            // The old entry launches the old exe; swap it for one that launches this one.
            if (AutoStart.RemoveLegacyEntry(LegacyName))
            {
                AutoStart.SetEnabled(true);
            }
        }
        catch
        {
        }
    }
}
