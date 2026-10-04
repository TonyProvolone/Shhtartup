using System.Diagnostics;
using Microsoft.Win32;

namespace Shhtartup;

internal readonly record struct InstallOptions(bool DesktopShortcut, bool StartWithWindows);

// Shhtartup.exe is its own installer. Run from anywhere other than the install folder:
//   - not installed yet: the setup window asks where to install, then the copy there starts;
//   - already installed: it installs over the saved folder without asking, then starts that copy.
// Installing registers the app under Settings > Apps (per-user, no admin) with "--uninstall" as its
// uninstall command, and adds a Start menu shortcut.
internal static class Installer
{
    public const string ExeName = "Shhtartup.exe";
    public const string AfterInstallArg = "--after-install";
    public const string UninstallArg = "--uninstall";

    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Shhtartup";
    private const string RepoUrl = "https://github.com/TonyProvolone/Shhtartup";

    public static string DefaultLocation { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Shhtartup");

    // Settings, remembered games and their backups.
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shhtartup");

    private static readonly string StartMenuShortcut = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Shhtartup.lnk");

    private static readonly string DesktopShortcut = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Shhtartup.lnk");

    // Everything Shhtartup (or its updater) ever puts in the install folder.
    private static readonly string[] ProgramFiles =
        [ExeName, ExeName + ".old", ExeName + ".new", "Shhtartup.pdb"];

    // Returns true if this process should go on to run the tray app.
    public static bool RunSetupIfNeeded()
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
        {
            return true;
        }

        var installedExe = InstalledExe();
        if (installedExe is not null && SamePath(exe, installedExe))
        {
            RefreshRegistration(installedExe);
            return true;
        }

        if (installedExe is not null)
        {
            // Already installed: always install to the same place, no questions asked.
            var folder = Path.GetDirectoryName(installedExe)!;
            if (InstallTo(folder) is { } error)
            {
                SetupWindow.ShowMessage("Failed to update", error);
                return false;
            }
            Launch(installedExe, AfterInstallArg);
            return false;
        }

        // Fresh install: the setup window calls InstallTo with the chosen folder.
        if (SetupWindow.RunInstall(DefaultLocation) is { } chosen)
        {
            Launch(Path.Combine(chosen, ExeName), AfterInstallArg);
        }
        return false;
    }

    // options: the setup window's checkboxes on a fresh install; null when installing over an
    // existing copy. Returns null on success, or a sentence explaining what went wrong.
    public static string? InstallTo(string folder, InstallOptions? options = null)
    {
        var source = Environment.ProcessPath!;
        var target = Path.Combine(folder, ExeName);

        if (IsUnder(folder, Environment.SpecialFolder.ProgramFiles) || IsUnder(folder, Environment.SpecialFolder.ProgramFilesX86))
        {
            return "Cannot update inside Program Files. Choose a folder in your user folder instead.";
        }
        if (IsUnder(folder, Environment.SpecialFolder.Windows))
        {
            return "Choose a folder outside the Windows folder.";
        }

        try
        {
            Directory.CreateDirectory(folder);

            // The running copy holds its exe open; close it first.
            SingleInstance.CloseRunning(TimeSpan.FromSeconds(5));

            if (!SamePath(source, target))
            {
                // Copy beside the target first, then swap, so a failed copy never leaves a broken exe.
                // (Renaming also works if the old exe is somehow still running.)
                var staged = target + ".new";
                File.Copy(source, staged, overwrite: true);
                if (File.Exists(target))
                {
                    File.Move(target, target + ".old", overwrite: true);
                }
                File.Move(staged, target);
                TryDelete(target + ".old");
            }

            Register(target);
            TryCreateShortcut(StartMenuShortcut, target);

            if (options is { } chosen)
            {
                if (chosen.DesktopShortcut)
                {
                    TryCreateShortcut(DesktopShortcut, target);
                }
                SetStartWithWindows(chosen.StartWithWindows, target);
            }
            else
            {
                // Installing over an existing copy keeps the choices made then: refresh a desktop
                // shortcut only if there is one, and point startup at this copy only if it was on.
                if (File.Exists(DesktopShortcut))
                {
                    TryCreateShortcut(DesktopShortcut, target);
                }
                if (AutoStart.HasEntry())
                {
                    AutoStart.SetEnabled(true, target);
                }
            }
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return $"Shhtartup doesn't have permission to write to {folder}. Choose a folder in your user folder instead.";
        }
        catch (Exception)
        {
            return $"Shhtartup couldn't be installed to {folder}. Check the folder and try again.";
        }
    }

    // "--uninstall" (from Settings > Apps): confirm, then remove the program, its settings and every
    // trace of it in the registry and Start menu.
    public static void Uninstall()
    {
        if (!SetupWindow.ConfirmUninstall())
        {
            return;
        }

        SingleInstance.CloseRunning(TimeSpan.FromSeconds(5));

        var folder = InstalledExe() is { } installed
            ? Path.GetDirectoryName(installed)!
            : Path.GetDirectoryName(Environment.ProcessPath!)!;

        try { AutoStart.SetEnabled(false); } catch (Exception) { }
        try { AutoStart.RemoveLegacyEntry("VolumeGuard"); } catch (Exception) { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false); } catch (Exception) { }
        TryDelete(StartMenuShortcut);
        TryDelete(DesktopShortcut);
        try
        {
            if (Directory.Exists(DataDir))
            {
                Directory.Delete(DataDir, recursive: true);
            }
        }
        catch (Exception)
        {
            // A file is open somewhere -- leave what can't be removed.
        }

        foreach (var name in ProgramFiles)
        {
            TryDelete(Path.Combine(folder, name));
        }

        SetupWindow.ShowMessage("Successfully uninstalled",
            "All settings have removed.", success: true);

        // This exe is still running, so it can't delete itself or its folder. A hidden cmd.exe does
        // it a moment after this process exits. Only Shhtartup's own files are deleted, and the
        // folder only if that leaves it empty, in case it was shared with other files.
        DeleteAfterExit(folder);
    }

    private static string? InstalledExe()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
            if (key?.GetValue("InstallLocation") is string folder)
            {
                var exe = Path.Combine(folder, ExeName);
                return File.Exists(exe) ? exe : null;
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    // The Settings > Apps entry. Per-user (HKCU), so installing never needs admin.
    private static void Register(string exe)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
        key.SetValue("DisplayName", "Shhtartup");
        key.SetValue("DisplayVersion", UpdateChecker.CurrentVersion.ToString());
        key.SetValue("DisplayIcon", exe);
        key.SetValue("Publisher", "Shhtartup");
        key.SetValue("InstallLocation", Path.GetDirectoryName(exe)!);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        key.SetValue("UninstallString", $"\"{exe}\" {UninstallArg}");
        key.SetValue("URLInfoAbout", RepoUrl);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
    }

    // After the in-app updater swaps the exe, the version shown in Settings > Apps catches up here.
    private static void RefreshRegistration(string exe)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath, writable: true);
            if (key is null)
            {
                return;
            }
            var version = UpdateChecker.CurrentVersion.ToString();
            if (key.GetValue("DisplayVersion") as string != version)
            {
                key.SetValue("DisplayVersion", version);
                key.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
            }
        }
        catch (Exception)
        {
        }
    }

    // Saved to settings as well as applied, so the app's own first-run default (startup on) doesn't
    // override the choice, and the Settings window's "Run on startup" toggle shows it.
    private static void SetStartWithWindows(bool enabled, string exe)
    {
        LegacyMigration.Run(); // Before Settings.Load creates the data folder, so old settings still carry over.
        Settings.Load();
        Settings.Current.StartWithWindows = enabled;
        Settings.Save();
        AutoStart.SetEnabled(enabled, exe);
    }

    private static void TryCreateShortcut(string linkPath, string target)
    {
        try
        {
            ShellHelpers.CreateShortcut(linkPath, target, "Sets the Windows volume on newly installed games/apps to a more reasonable volume when they launch");
        }
        catch (Exception)
        {
            // A missing shortcut isn't worth failing the install over.
        }
    }

    private static void DeleteAfterExit(string folder)
    {
        var files = string.Join(" ", ProgramFiles.Select(name => $"\"{Path.Combine(folder, name)}\""));

        // Up to ~10 tries, a second apart: delete the files, then the folder (rd without /s only
        // succeeds once it's empty). Retrying covers this exe still exiting, and a deleted file that
        // lingers for a moment while something (e.g. antivirus) still has it open.
        var script = $"for /l %i in (1,1,10) do @if exist \"{folder}\" " +
                     $"(ping -n 2 127.0.0.1 >nul & del /f /q {files} >nul 2>&1 & rd \"{folder}\" >nul 2>&1)";
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c \"{script}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetTempPath(),
            });
        }
        catch (Exception)
        {
        }
    }

    private static void Launch(string exe, string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
        }
        catch (Exception)
        {
        }
    }

    private static bool IsUnder(string path, Environment.SpecialFolder special)
    {
        var root = Environment.GetFolderPath(special);
        if (string.IsNullOrEmpty(root))
        {
            return false;
        }
        var full = Path.GetFullPath(path).TrimEnd('\\') + "\\";
        return full.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
        }
    }
}
