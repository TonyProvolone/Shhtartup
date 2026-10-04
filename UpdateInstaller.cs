using System.Diagnostics;
using System.Security.Cryptography;
using Tinnitdown.Interop;

namespace Tinnitdown;

// Downloads a release's exe next to the running one and swaps them. Windows lets a running exe be
// renamed (not overwritten), so the current file becomes Tinnitdown.exe.old and the download takes its
// name -- the new version runs from the next launch, or right away if the user chose to restart.
// The .old file is deleted on the next launch, once nothing is running from it.
internal static class UpdateInstaller
{
    private const string NewSuffix = ".new";
    private const string OldSuffix = ".old";

    public enum Stage { Downloading, Verifying, Installing, Restarting }

    // Passed to the relaunched copy so it waits for this one to exit (SingleInstance) and says so.
    public const string AfterUpdateArg = "--after-update";

    // Verifying and installing take milliseconds; hold each on screen long enough to read.
    private const int MinStageMs = 600;

    public static bool IsBusy { get; private set; }

    // Set once an update has been swapped in this session (it runs from the next launch).
    public static Version? InstalledVersion { get; private set; }

    private static ReleaseInfo? _release;
    private static bool _restartWhenDone;

    // Written by the background download, read on the UI thread once WM_UPDATE_INSTALL_DONE arrives.
    private static volatile string? _error;

    public static void CleanUp()
    {
        if (ExePath() is { } exe)
        {
            TryDelete(exe + OldSuffix);
            TryDelete(exe + NewSuffix);
        }
    }

    public static void Start(ReleaseInfo release, bool restartWhenDone)
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        _release = release;
        _restartWhenDone = restartWhenDone;
        _error = null;

        UpdateToast.ShowProgress(release.Version, restartWhenDone);

        Task.Run(async () =>
        {
            _error = await DownloadAndSwapAsync(release, restartWhenDone);
            User32.PostMessageW(TrayIcon.Hwnd, TrayIcon.WM_UPDATE_INSTALL_DONE, 0, 0);
        });
    }

    public static void OnInstallDone()
    {
        IsBusy = false;
        UpdateToast.Hide();
        var release = _release!;

        if (_error is { } error)
        {
            UpdateChecker.Notify("Unable to install latest update", $"{error} Click to download manually.",
                warning: true, clickUrl: release.PageUrl);
            return;
        }

        InstalledVersion = release.Version;

        if (_restartWhenDone)
        {
            try
            {
                Process.Start(new ProcessStartInfo(ExePath()!, AfterUpdateArg) { UseShellExecute = false });
                TrayIcon.ExitApp();
                return;
            }
            catch (Exception)
            {
                // Fall through: the update is in place, it just didn't relaunch.
            }
        }

        UpdateChecker.Notify("Update installed", $"{release.Version} starts the next time you open Tinnitdown.");
    }

    // Returns null on success, or a sentence describing what went wrong.
    private static async Task<string?> DownloadAndSwapAsync(ReleaseInfo release, bool restartWhenDone)
    {
        var exe = ExePath();
        if (exe is null)
        {
            return "Couldn't find its exe to replace.";
        }

        var newPath = exe + NewSuffix;
        var oldPath = exe + OldSuffix;

        try
        {
            using var http = UpdateChecker.CreateHttpClient(TimeSpan.FromSeconds(30));
            using var stall = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var response = await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, stall.Token);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? release.Size;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;
            var lastPercent = -1;

            await using (var source = await response.Content.ReadAsStreamAsync(stall.Token))
            await using (var file = new FileStream(newPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, stall.Token)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), stall.Token);
                    hash.AppendData(buffer, 0, read);
                    received += read;

                    var percent = total > 0 ? (int)(received * 100 / total) : 0;
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        Report(Stage.Downloading, percent);
                    }
                }
            }

            await EnterStageAsync(Stage.Verifying);
            if (release.Size > 0 && received != release.Size)
            {
                TryDelete(newPath);
                return "Download incomplete.";
            }
            if (release.Sha256 is { } expected &&
                !Convert.ToHexStringLower(hash.GetHashAndReset()).Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(newPath);
                return "Download didn't match release's checksum.";
            }

            await EnterStageAsync(Stage.Installing);
            File.Move(exe, oldPath, overwrite: true);
            try
            {
                File.Move(newPath, exe);
            }
            catch
            {
                File.Move(oldPath, exe);
                throw;
            }

            if (restartWhenDone)
            {
                await EnterStageAsync(Stage.Restarting);
            }
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(newPath);
            return $"Permission to replace files in {Path.GetDirectoryName(exe)} denied.";
        }
        catch (IOException)
        {
            TryDelete(newPath);
            return "Unable to replace exe.";
        }
        catch (Exception)
        {
            TryDelete(newPath);
            return "Unable to download update.";
        }
    }

    // Called from the background download; the toast is updated on the UI thread.
    private static void Report(Stage stage, int percent) =>
        User32.PostMessageW(TrayIcon.Hwnd, TrayIcon.WM_UPDATE_PROGRESS, (nuint)percent, (nint)stage);

    private static Task EnterStageAsync(Stage stage)
    {
        Report(stage, 100);
        return Task.Delay(MinStageMs);
    }

    // The running exe, unless it's a host like dotnet.exe (e.g. during `dotnet run`).
    private static string? ExePath()
    {
        var path = Environment.ProcessPath;
        if (path is null || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Still in use (e.g. the previous version hasn't exited yet) -- retried on the next launch.
        }
    }
}
