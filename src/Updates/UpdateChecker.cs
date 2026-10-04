using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Shhtartup.Interop;

namespace Shhtartup;

internal sealed record ReleaseInfo(Version Version, string PageUrl, string? DownloadUrl, long Size, string? Sha256);

// Asks GitHub for the repo's latest release and compares its tag (e.g. "v1.2.0") with this build's
// <Version>. Runs silently a few seconds after every launch (only an available update is shown) and
// on demand from "Check for updates" in the tray menu (every outcome is reported).
internal static class UpdateChecker
{
    private const string Repo = "TonyProvolone/Shhtartup";
    private const string LatestReleaseUrl = $"https://api.github.com/repos/{Repo}/releases/latest";
    private const string ReleasesPageUrl = $"https://github.com/{Repo}/releases";
    private const string AssetName = "Shhtartup.exe";

    public const uint StartupDelayMs = 5_000;
    private const uint RetryDelayMs = 60_000;
    private const int MaxStartupAttempts = 3;

    private enum Outcome { UpdateAvailable, UpToDate, NoReleases, BadTag, Failed }

    private sealed record CheckResult(Outcome Outcome, ReleaseInfo? Release = null, string? Tag = null, string? PageUrl = null);

    public static Version CurrentVersion { get; } =
        Normalize(typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0));

    private static bool _checking;
    private static bool _silent;
    private static int _startupAttempts;

    // Written by the background check, read on the UI thread once WM_UPDATE_CHECK_DONE arrives.
    private static volatile CheckResult? _pending;

    // Opened when the user clicks a tray notification that has somewhere to go.
    private static string? _clickUrl;

    // TimerIds.UpdateCheckTimer: the delayed check after launch, and its retries.
    public static void OnStartupTimer()
    {
        User32.KillTimer(TrayIcon.Hwnd, TimerIds.UpdateCheckTimer);
        UpdateInstaller.CleanUp();
        _startupAttempts++;
        Check(silent: true);
    }

    public static void CheckNow() => Check(silent: false);

    private static void Check(bool silent)
    {
        if (_checking)
        {
            // A manual check during the startup check: report that one's result instead.
            _silent &= silent;
            return;
        }
        _checking = true;
        _silent = silent;

        Task.Run(async () =>
        {
            _pending = await FetchAsync();
            User32.PostMessageW(TrayIcon.Hwnd, TrayIcon.WM_UPDATE_CHECK_DONE, 0, 0);
        });
    }

    public static void OnCheckDone()
    {
        _checking = false;
        var result = _pending;
        _pending = null;
        if (result is null)
        {
            return;
        }

        var silent = _silent;
        switch (result.Outcome)
        {
            case Outcome.UpdateAvailable:
                OnUpdateAvailable(result.Release!, silent);
                break;

            case Outcome.UpToDate when !silent:
                Notify("You're up to date", $"{CurrentVersion} is the latest version.");
                break;

            case Outcome.NoReleases when !silent:
                Notify("No releases yet", "No new releases published.");
                break;

            case Outcome.BadTag when !silent:
                Notify("Couldn't read latest version",
                    $"Latest release is tagged \"{result.Tag}\". Click to view it.", warning: true, result.PageUrl);
                break;

            case Outcome.Failed when silent:
                // Often the network isn't up yet right after logging in -- try again shortly.
                if (_startupAttempts < MaxStartupAttempts)
                {
                    User32.SetTimer(TrayIcon.Hwnd, TimerIds.UpdateCheckTimer, RetryDelayMs, 0);
                }
                break;

            case Outcome.Failed:
                Notify("Unable to check for updates",
                    "GitHub couldn't be reached. Check your connection and try again.", warning: true);
                break;
        }
    }

    private static void OnUpdateAvailable(ReleaseInfo release, bool silent)
    {
        if (UpdateInstaller.InstalledVersion is { } installed && installed >= release.Version)
        {
            if (!silent)
            {
                Notify("Update ready", $"{installed} is installed and and ready the next time you start.");
            }
            return;
        }

        if (UpdateInstaller.IsBusy)
        {
            return;
        }

        if (release.DownloadUrl is null)
        {
            // The release has no exe attached -- point at the release page instead.
            Notify("Update available",
                $"Shhtartup {release.Version} is available (you have {CurrentVersion}). Click to view.",
                clickUrl: release.PageUrl);
            return;
        }

        UpdateToast.ShowOffer(release, deferWhileBusy: silent);
    }

    // Tray notification (a Windows toast on 10/11). clickUrl opens in the browser if it's clicked.
    public static void Notify(string title, string text, bool warning = false, string? clickUrl = null)
    {
        _clickUrl = clickUrl;
        TrayIcon.ShowNotification(title, text, warning);
    }

    public static void OnNotificationClicked()
    {
        if (_clickUrl is not { } url)
        {
            return;
        }
        _clickUrl = null;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No default browser registered -- nothing useful to do.
        }
    }

    public static HttpClient CreateHttpClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        // GitHub's API rejects requests without a User-Agent.
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Shhtartup/{CurrentVersion}");
        return http;
    }

    private static async Task<CheckResult> FetchAsync()
    {
        try
        {
            using var http = CreateHttpClient(TimeSpan.FromSeconds(15));
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            using var response = await http.GetAsync(LatestReleaseUrl);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // /releases/latest 404s until a non-draft, non-prerelease release exists.
                return new(Outcome.NoReleases);
            }
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
            var pageUrl = (root.TryGetProperty("html_url", out var html) ? html.GetString() : null) ?? ReleasesPageUrl;

            if (!TryParseVersion(tag, out var latest))
            {
                return new(Outcome.BadTag, Tag: tag, PageUrl: pageUrl);
            }
            if (latest <= CurrentVersion)
            {
                return new(Outcome.UpToDate);
            }

            var (downloadUrl, size, sha256) = FindExeAsset(root);
            return new(Outcome.UpdateAvailable, new ReleaseInfo(latest, pageUrl, downloadUrl, size, sha256));
        }
        catch (Exception)
        {
            return new(Outcome.Failed);
        }
    }

    // Prefers an asset named Shhtartup.exe, else the first .exe attached to the release.
    private static (string? Url, long Size, string? Sha256) FindExeAsset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return (null, 0, null);
        }

        JsonElement? chosen = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
            if (name.Equals(AssetName, StringComparison.OrdinalIgnoreCase))
            {
                chosen = asset;
                break;
            }
            if (chosen is null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                chosen = asset;
            }
        }

        if (chosen is not { } a || !a.TryGetProperty("browser_download_url", out var url))
        {
            return (null, 0, null);
        }

        var size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0;

        // GitHub publishes "sha256:<hex>" for each asset.
        string? sha256 = null;
        if (a.TryGetProperty("digest", out var d) && d.GetString() is { } digest &&
            digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            sha256 = digest["sha256:".Length..];
        }

        return (url.GetString(), size, sha256);
    }

    // Accepts tags like "v1.2", "1.2.3" or "v1.2.3-beta" (the suffix is ignored).
    private static bool TryParseVersion(string tag, out Version version)
    {
        var s = tag.Trim().TrimStart('v', 'V');
        var cut = s.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0)
        {
            s = s[..cut];
        }
        if (!s.Contains('.'))
        {
            s += ".0";
        }

        if (Version.TryParse(s, out var parsed))
        {
            version = Normalize(parsed);
            return true;
        }
        version = new Version(0, 0, 0);
        return false;
    }

    // Compare as major.minor.patch so "1.2" equals "1.2.0" and the assembly's 4th component is ignored.
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
