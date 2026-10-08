using System.Windows;

namespace HowlingWhispers.CodaLauncher;

public partial class App : Application
{
    internal const string LauncherVersion = "0.7.2";

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
        try
        {
            using var checkTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            update = await SelfUpdater.CheckAsync(LauncherVersion, checkTimeout.Token);
        }
        catch
        {
            // Update checks must never stop an installed launcher from opening.
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

        var window = new MainWindow();
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }
}

