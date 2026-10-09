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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (SelfUpdater.TryRunApplyMode(e.Args))
        {
            Shutdown();
            return;
        }

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

        LauncherUpdateInfo? update = null;
        // A successful Play update must resume its one-shot launch without
        // displaying another preflight update prompt.
        if (!e.Args.Contains("--resume-play", StringComparer.Ordinal))
        {
            try
            {
                using var checkTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                update = await SelfUpdater.CheckAsync(LauncherVersion, checkTimeout.Token);
            }
            catch
            {
                // Update checks must never prevent launcher startup.
            }
        }

        if (update is not null)
        {
            var updateWindow = new UpdateWindow(update);
            MainWindow = updateWindow;
            updateWindow.ShowDialog();

            if (updateWindow.RestartRequested)
            {
                Shutdown();
                return;
            }

            if (!updateWindow.ContinueWithoutUpdate)
            {
                Shutdown();
                return;
            }
        }

        var window = new MainWindow(resumePlayOnReady: e.Args.Contains("--resume-play", StringComparer.Ordinal));
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }
}

