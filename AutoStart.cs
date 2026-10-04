using Microsoft.Win32;

namespace Tinnitdown;

internal static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Tinnitdown";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(ValueName) as string;
        if (value is null)
        {
            return false;
        }

        var exePath = Environment.ProcessPath;
        return exePath is not null && value.Trim('"').Equals(exePath, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
        {
            var exePath = Environment.ProcessPath;
            if (exePath is not null)
            {
                key.SetValue(ValueName, $"\"{exePath}\"");
            }
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    // Removes a startup entry left by an older name of this app. Returns whether one existed.
    public static bool RemoveLegacyEntry(string legacyValueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key?.GetValue(legacyValueName) is null)
        {
            return false;
        }

        key.DeleteValue(legacyValueName, throwOnMissingValue: false);
        return true;
    }
}
