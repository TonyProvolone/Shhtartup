using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Tinnitdown.Interop;

namespace Tinnitdown;

// "Check for updates" in the tray menu: asks GitHub for the repo's latest release, compares its tag
// (e.g. "v1.2.0") with this build's <Version>, and reports the result as a tray notification.
internal static class UpdateChecker
{
    private const string Repo = "TonyProvolone/Tinnitdown";
    private const string LatestReleaseUrl = $"https://api.github.com/repos/{Repo}/releases/latest";
    private const string ReleasesPageUrl = $"https://github.com/{Repo}/releases";

    private sealed record Result(string Title, string Text, string? ClickUrl, bool IsError);

    private static bool _checking;

    // Written by the background check, read on the UI thread once WM_UPDATE_CHECK_DONE arrives.
    private static volatile Result? _pending;

    // Opened when the user clicks the notification (only set when there's an update to fetch).
    private static string? _clickUrl;

    public static void CheckNow()
    {
        if (_checking)
        {
            return;
        }
        _checking = true;

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

        _clickUrl = result.ClickUrl;
        TrayIcon.ShowNotification(result.Title, result.Text, result.IsError);
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

    private static async Task<Result> FetchAsync()
    {
        var current = CurrentVersion();
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            // GitHub's API rejects requests without a User-Agent.
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"Tinnitdown/{current}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            using var response = await http.GetAsync(LatestReleaseUrl);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // /releases/latest 404s until a non-draft, non-prerelease release exists.
                return new("No releases yet", "There's no published release to update to yet.", null, false);
            }
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
            var pageUrl = (root.TryGetProperty("html_url", out var html) ? html.GetString() : null) ?? ReleasesPageUrl;

            if (!TryParseVersion(tag, out var latest))
            {
                return new("Couldn't read the latest version",
                    $"The latest release is tagged \"{tag}\". Click to view it.", pageUrl, true);
            }

            return latest > current
                ? new("Update available",
                    $"Tinnitdown {latest} is available (you have {current}). Click to download it.", pageUrl, false)
                : new("You're up to date", $"Tinnitdown {current} is the latest version.", null, false);
        }
        catch (Exception)
        {
            return new("Couldn't check for updates",
                "GitHub couldn't be reached. Check your connection and try again.", null, true);
        }
    }

    private static Version CurrentVersion()
    {
        var v = typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0);
        return Normalize(v);
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
