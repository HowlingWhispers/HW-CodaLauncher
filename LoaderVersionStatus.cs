namespace HowlingWhispers.CodaLauncher;

/// <summary>
/// The Nightly version panel can report an update only after a successful
/// Nightly release check. A failed query is not evidence of an outdated runtime.
/// </summary>
internal static class LoaderVersionStatus
{
    internal static string Nightly(string? installed, string? latest, bool ready)
    {
        if (!ready) return "NOT INSTALLED";
        if (!Version.TryParse(latest, out var published)) return "CHECK UNAVAILABLE";
        if (!Version.TryParse(installed, out var current)) return "VERSION UNKNOWN";
        int comparison = current.CompareTo(published);
        return comparison < 0 ? "UPDATE AVAILABLE" :
            comparison > 0 ? "AHEAD OF RELEASE" : "CURRENT";
    }
}
