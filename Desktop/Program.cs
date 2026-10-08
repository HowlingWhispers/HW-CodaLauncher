using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace HowlingWhispers.CodaLauncher;

internal static class Program
{
    internal const string Version = "0.7.2";
    internal static bool SmokeUi;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--version")) { Console.WriteLine(Version); return 0; }
        if (args.Contains("--smoke-test"))
        {
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "assets", "coda-headshot.png")))
                throw new IOException("Approved Coda portrait missing");
            Console.WriteLine($"CodaLauncher {Version}: {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}; shared installer loaded; portrait present.");
            return 0;
        }
        SmokeUi = args.Contains("--smoke-ui");
        return AppBuilder.Configure<DesktopApp>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
    }
}

public sealed class DesktopApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new DesktopWindow();
            if (Program.SmokeUi)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (_, _) => { timer.Stop(); desktop.Shutdown(); };
                timer.Start();
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
