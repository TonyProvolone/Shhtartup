using System.Collections.Frozen;

namespace Tinnitdown;

internal static class LauncherCatalog
{
    // A process with one of these as an ancestor was started by a game launcher.
    // (Battle.net's "agent.exe" is deliberately absent: it's only the updater, games are launched by
    // Battle.net.exe, and the name is generic enough to collide with unrelated software.)
    public static readonly FrozenSet<string> LauncherExeNames = new[]
    {
        "steam.exe",
        "epicgameslauncher.exe",
        "galaxyclient.exe",
        "galaxyclientservice.exe",
        "upc.exe",
        "ubisoftconnect.exe",
        "uplayservice.exe",
        "origin.exe",
        "eadesktop.exe",
        "eabackgroundservice.exe",
        "battle.net.exe",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // Launcher helpers, overlays, crash reporters, anti-cheat and installers. These still count as
    // links in a launcher's process chain, but are never treated as games themselves -- some of them
    // (store pages, overlays) play audio that shouldn't be clamped.
    public static readonly FrozenSet<string> ExcludedExeNames = new[]
    {
        // Steam
        "steamwebhelper.exe", "steamservice.exe", "steamerrorreporter.exe", "steamerrorreporter64.exe",
        "gameoverlayui.exe", "gameoverlayui64.exe", "steamsysinfo.exe", "steam_monitor.exe",
        // Epic
        "epicwebhelper.exe", "epiconlineservices.exe", "epiconlineserviceshost.exe",
        "epiconlineservicesuserhelper.exe", "unrealcefsubprocess.exe", "crashreportclient.exe",
        // GOG
        "galaxyclient helper.exe", "galaxycommunication.exe", "gog galaxy notifications renderer.exe",
        "galaxyupdater.exe",
        // Ubisoft
        "uplaywebcore.exe", "upc_crashreporter.exe", "uplaycrashreporter.exe",
        // EA / Origin
        "eacefsubprocess.exe", "ealocalhostsvc.exe", "eaconnect_microsoft.exe", "originwebhelperservice.exe",
        "originclientservice.exe", "originer.exe", "igoproxy.exe", "igoproxy64.exe",
        // Battle.net
        "battle.net helper.exe", "blizzarderror.exe", "blizzardbrowser.exe", "agent.exe",
        // Generic helpers that games and launchers spawn
        "crashpad_handler.exe", "unitycrashhandler32.exe", "unitycrashhandler64.exe",
        "easyanticheat.exe", "easyanticheat_eos.exe", "easyanticheat_eos_setup.exe",
        "beservice.exe", "beservice_x64.exe", "qtwebengineprocess.exe", "cefsharp.browsersubprocess.exe",
        "msedgewebview2.exe", "werfault.exe", "conhost.exe", "cmd.exe",
        "vc_redist.x64.exe", "vc_redist.x86.exe", "dxsetup.exe", "dxwebsetup.exe",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsLauncherOrExcluded(string exeName) =>
        LauncherExeNames.Contains(exeName) || ExcludedExeNames.Contains(exeName);
}
