namespace Tinnitdown;

internal static class HandledPidTracker
{
    private static readonly HashSet<uint> _handled = new();

    public static bool IsHandled(uint pid) => _handled.Contains(pid);

    public static void MarkHandled(uint pid) => _handled.Add(pid);

    public static void Prune(HashSet<uint> livePids)
    {
        _handled.IntersectWith(livePids);
    }
}
