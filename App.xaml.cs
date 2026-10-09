using System.Reflection;
using System.Windows;

namespace HowlingWhispers.CodaLauncher;

public partial class App : Application
{
    // Keep updater checks aligned with the actual compiled release.
    internal static readonly string LauncherVersion =
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+', 2)[0]
        ?? throw new InvalidOperationException("CodaLauncher is missing release version metadata.");

    internal static bool IsSmokeTest { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var probeResult = SelfUpdater.RunStagedProbe(e.Args);
        if (probeResult.HasValue)
        {
            Shutdown(probeResult.Value);
            return;
        }
        if (SelfUpdater.TryRunApplyMode(e.Args))
        {
            Shutdown();
            return;
        }
        SelfUpdater.ConfigurePostUpdateHealth(e.Args);

        IsSmokeTest = e.Args.Contains("--smoke-ui");
        if (IsSmokeTest)
        {
            var timeout = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            timeout.Tick += (_, _) => { timeout.Stop(); Shutdown(1); };
            timeout.Start();
            var smokeWindow = new MainWindow();
            MainWindow = smokeWindow;
            smokeWindow.Show();
            return;
        }

        // The MainWindow checks for available updates in the background.
        // Pressing Play performs a fresh check and handles verified updates.
        // Never block startup with a modal update window.

        var window = new MainWindow(resumePlayOnReady: e.Args.Contains("--resume-play", StringComparer.Ordinal));
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }
}

