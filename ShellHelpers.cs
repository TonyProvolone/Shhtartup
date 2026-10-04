using System.Runtime.InteropServices;
using Shhtartup.Interop;

namespace Shhtartup;

// The Windows folder picker and .lnk shortcuts, via the shell's COM objects.
internal static class ShellHelpers
{
    // Returns the chosen folder, or null if the user cancelled. Opens at the nearest existing
    // folder to initialPath.
    public static string? PickFolder(nint owner, string title, string? initialPath)
    {
        if (Ole32.CoCreateInstance(ShellGuids.CLSID_FileOpenDialog, 0, Ole32.CLSCTX_INPROC_SERVER,
                ShellGuids.IID_IFileDialog, out IFileDialog dialog) < 0)
        {
            return null;
        }

        dialog.GetOptions(out var options);
        dialog.SetOptions(options | ShellGuids.FOS_PICKFOLDERS | ShellGuids.FOS_FORCEFILESYSTEM | ShellGuids.FOS_PATHMUSTEXIST);
        dialog.SetTitle(title);
        dialog.SetOkButtonLabel("Select folder");

        var start = NearestExistingFolder(initialPath);
        if (start is not null &&
            Shell32.SHCreateItemFromParsingName(start, 0, ShellGuids.IID_IShellItem, out var startItem) >= 0)
        {
            dialog.SetFolder(startItem);
        }

        if (dialog.Show(owner) < 0 || dialog.GetResult(out var item) < 0)
        {
            return null;
        }

        if (item.GetDisplayName(ShellGuids.SIGDN_FILESYSPATH, out var namePtr) < 0 || namePtr == 0)
        {
            return null;
        }
        try
        {
            return Marshal.PtrToStringUni(namePtr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(namePtr);
        }
    }

    // Writes (or replaces) a shortcut at linkPath that starts targetPath.
    public static bool CreateShortcut(string linkPath, string targetPath, string description)
    {
        if (Ole32.CoCreateInstance(ShellGuids.CLSID_ShellLink, 0, Ole32.CLSCTX_INPROC_SERVER,
                ShellGuids.IID_IShellLinkW, out IShellLinkW link) < 0)
        {
            return false;
        }

        link.SetPath(targetPath);
        link.SetWorkingDirectory(Path.GetDirectoryName(targetPath)!);
        link.SetIconLocation(targetPath, 0);
        link.SetDescription(description);

        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        return ((IPersistFile)link).Save(linkPath, true) >= 0;
    }

    private static string? NearestExistingFolder(string? path)
    {
        try
        {
            while (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
            {
                path = Path.GetDirectoryName(path);
            }
        }
        catch (Exception)
        {
            return null;
        }
        return string.IsNullOrEmpty(path) ? null : path;
    }
}
