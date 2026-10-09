namespace HowlingWhispers.CodaLauncher;

/// <summary>
/// Play updates only add-ons the player already opted into and that the
/// launcher can authenticate. An absent/edited add-on is never auto-installed
/// or overwritten. Stable runs through its separate update path.
/// </summary>
internal static class NightlyPlayUpdatePolicy
{
    internal readonly record struct Plan(
        bool UpdateBuildCraft,
        bool UpdateQuiet,
        bool WarnUnmanagedBuildCraft,
        bool WarnUnmanagedQuiet);

    internal static Plan Select(bool buildCraftExists, bool buildCraftManaged,
        bool quietExists, bool quietManaged)
    {
        return new Plan(
            buildCraftExists && buildCraftManaged,
            quietExists && quietManaged,
            buildCraftExists && !buildCraftManaged,
            quietExists && !quietManaged);
    }
}
