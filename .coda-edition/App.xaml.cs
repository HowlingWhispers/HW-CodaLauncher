using System.Reflection;
using System.Windows;
namespace HowlingWhispers.CodaLauncher;
public partial class App : Application
{
    internal static readonly string LauncherVersion = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+', 2)[0];
    internal static bool IsSmokeTest { get; private set; }
    internal static bool AllowLauncherUpdates => false;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        IsSmokeTest = e.Args.Contains("--smoke-ui");
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var window = new MainWindow(); MainWindow = window; window.Show();
        if (IsSmokeTest) {
            var timeout = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            timeout.Tick += (_, _) => { timeout.Stop(); Shutdown(1); }; timeout.Start();
        }
    }
}
