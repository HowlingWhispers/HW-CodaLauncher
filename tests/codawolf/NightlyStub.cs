namespace HowlingWhispers.CodaLauncher;
// The real launcher owns the Nightly profile root. Tests inject a disposable root.
internal sealed class NightlyBuildInstaller
{
    public static string GameRoot => throw new InvalidOperationException("Tests must inject an isolated game root.");
}
