namespace HowlingWhispers.CodaLauncher;

/// <summary>
/// The previous BuildCraft implementation has been deleted from HW-Mods.
/// Its public Nightly releases must never be installed or updated again.
/// Existing JARs require user-approved uninstall to protect saved worlds.
/// </summary>
internal static class NightlyPlayUpdatePolicy
{
    internal readonly record struct Plan(
        bool UpdateBuildCraft,
        bool UpdateQuiet,
        bool RetiredBuildCraftPresent,
        bool WarnUnmanagedQuiet);

    internal static Plan Select(bool buildCraftExists, bool buildCraftManaged,
        bool quietExists, bool quietManaged)
        => new(false, quietExists && quietManaged,
            buildCraftExists, quietExists && !quietManaged);
}
