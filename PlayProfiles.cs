using System.IO;

namespace HowlingWhispers.CodaLauncher;

/// <summary>Gameplay selection is independent of release channel. Classic paths never move.</summary>
internal static class PlayProfiles
{
    internal static string SelectedMode { get; private set; } = "classic";
    internal static void Validate(string mode, string channel)
    {
        if (mode is not ("classic" or "frontier")) throw new InvalidDataException("Choose Classic or Frontier.");
        if (mode == "frontier" && channel != "nightly")
            throw new InvalidDataException("Frontier is currently available in Nightly only. Select Nightly first.");
    }
    internal static void Select(string mode, string channel) { Validate(mode, channel); SelectedMode = mode; }
    internal static string NightlyGameRoot(string installRoot, string mode)
    {
        Validate(mode, "nightly");
        return mode == "classic" ? Path.Combine(installRoot, "nightly", "minecraft")
            : Path.Combine(installRoot, "nightly", "profiles", "frontier", "minecraft");
    }
    internal static bool CatalogVisible(string mode, string modId)
    {
        Validate(mode, "nightly");
        return mode == "frontier" ? modId == "github:howl-frontier" : modId != "github:howl-frontier";
    }
    internal static void Prepare(string gameRoot, string mode)
    {
        Validate(mode, "nightly");
        if (mode != "frontier") return;
        string directory = Path.Combine(gameRoot, "config"); Directory.CreateDirectory(directory);
        string marker = Path.Combine(directory, "howl-play-mode.txt");
        if (File.Exists(marker))
        {
            if (new FileInfo(marker).Length > 64 || File.ReadAllText(marker).Trim() != "frontier")
                throw new IOException("Frontier profile marker was modified. Existing files were preserved.");
            return;
        }
        using var output = new FileStream(marker, FileMode.CreateNew, FileAccess.Write);
        using var writer = new StreamWriter(output); writer.Write("frontier\n");
    }
}
