namespace HowlingWhispers.CodaLauncher;

/// <summary>
/// H.O.W.L. Coda Companion and HW Essentials are required, bundled components.
/// All other gameplay add-ons remain optional and user-managed. No online availability is inferred.
/// </summary>
internal sealed record OptionalModView(
    string Id, string Name, bool Recommended, bool Installed,
    bool Managed, bool NightlyOnly, string Version, string? ReleaseTag, bool Required = false);

internal static class OptionalModCatalog
{
    internal static IReadOnlyList<OptionalModView> Build(IReadOnlyList<ModInfo> mods,
        string gameRoot, bool nightly)
    {
        OptionalModView Item(string id, string name, bool recommended, bool nightlyOnly)
        {
            var found = mods.FirstOrDefault(x => x.Id == id);
            bool managed = found is not null &&
                (id == "hw_essentials"
                    ? ManagedMods.IsManagedInstall(gameRoot)
                    : found.ReleaseStatus == "Verified");
            return new OptionalModView(id, name, recommended, found is not null,
                managed, nightlyOnly, found?.Version ?? "",
                found?.ReleaseTag);
        }

        var list = new List<OptionalModView>();
        // Display only installed legacy BuildCraft so players can uninstall.
        // No new install option and no claimed availability.
        if (mods.Any(m => m.Id == "buildcraft_cml"))
            list.Add(Item("buildcraft_cml", "RETIRED BuildCraft prototype", false, true));
        list.Add(Item("coda_wolf", "Coda Wolf Companion", false, false)
            with { Required = true });
        list.Add(Item("hw_essentials", "HW Essentials", false, false)
            with { Required = true });
        return list;
    }
}
