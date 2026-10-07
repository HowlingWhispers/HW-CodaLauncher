using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace HowlingWhispers.CodaLauncher;

public partial class MainWindow : Window
{
    private const string Version = "0.1.0-prototype";
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private readonly SettingsStore _settingsStore = new();
    private readonly LogBuffer _logs = new();
    private readonly FeedService _feeds = new();
    private readonly ModScanner _mods = new();
    private readonly LauncherService _launcher;
    private LauncherSettings _settings = new();

    public MainWindow()
    {
        InitializeComponent();
        _launcher = new LauncherService(_logs, line => Dispatcher.Invoke(() => Send(new { type = "log", line })));
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings = _settingsStore.Load();
            _logs.Add("CodaLauncher starting.");
            Directory.CreateDirectory(AppPaths.WebViewData);

            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: AppPaths.WebViewData);
            await Browser.EnsureCoreWebView2Async(environment);

            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Browser.CoreWebView2.WebMessageReceived += OnWebMessage;
            Browser.CoreWebView2.NavigationStarting += OnNavigationStarting;
            Browser.CoreWebView2.NewWindowRequested += OnNewWindowRequested;

            Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "codalauncher.local",
                Path.Combine(AppContext.BaseDirectory, "web"),
                CoreWebView2HostResourceAccessKind.Allow);

            Browser.Source = new Uri("https://codalauncher.local/index.html");
        }
        catch (Exception ex)
        {
            MessageBox.Show("CodaLauncher could not start its interface.\n\n" + ex.Message, "CodaLauncher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            var action = root.TryGetProperty("action", out var a) ? a.GetString() : null;

            switch (action)
            {
                case "ready":
                case "refresh":
                    await SendState();
                    break;
                case "saveSettings":
                    if (root.TryGetProperty("settings", out var s))
                    {
                        _settings = s.Deserialize<LauncherSettings>(_json) ?? new();
                        _settingsStore.Save(_settings);
                        _logs.Add("Launcher settings saved.");
                        await SendState();
                    }
                    break;
                case "play":
                    await Play();
                    break;
                case "openLoaderFolder":
                    OpenLoaderFolder();
                    break;
                case "openExternal":
                    if (root.TryGetProperty("url", out var u)) OpenExternal(u.GetString());
                    break;
            }
        }
        catch (Exception ex)
        {
            var line = _logs.Add("UI request failed: " + ex.Message);
            Send(new { type = "log", line });
            Send(new { type = "error", message = ex.Message });
        }
    }

    private async Task SendState()
    {
        var loader = LoaderLocator.Resolve(_settings.LoaderPath);
        var mods = _mods.Scan(loader);
        var feed = await _feeds.FetchAsync(_settings.FeedUrl, CancellationToken.None);

        Send(new
        {
            type = "state",
            data = new
            {
                launcherVersion = Version,
                loaderPath = loader ?? "",
                loaderReady = LoaderLocator.IsReady(loader),
                modCount = mods.Count(m => m.Valid),
                mods,
                feed,
                settings = _settings,
                logs = _logs.Snapshot(),
                profile = new { cmlAccount = "Not configured", minecraftOwnership = "Not verified", discord = "Not linked", avatar = "Coming later" }
            }
        });
    }

    private Task Play()
    {
        var loader = LoaderLocator.Resolve(_settings.LoaderPath);
        if (!LoaderLocator.IsReady(loader))
        {
            Send(new { type = "launchStatus", ok = false, message = "Choose a valid CodaLoader folder in Settings first." });
            return Task.CompletedTask;
        }

        try
        {
            var pid = _launcher.Launch(loader!);
            Send(new { type = "launchStatus", ok = true, message = $"CodaLoader started as process {pid}." });
            if (_settings.CloseAfterLaunch) Close();
        }
        catch (Exception ex)
        {
            _logs.Add("Launch failed: " + ex.Message);
            Send(new { type = "launchStatus", ok = false, message = ex.Message });
        }
        return Task.CompletedTask;
    }

    private void OpenLoaderFolder()
    {
        var loader = LoaderLocator.Resolve(_settings.LoaderPath);
        if (string.IsNullOrWhiteSpace(loader) || !Directory.Exists(loader)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", loader) { UseShellExecute = true });
    }

    private static void OpenExternal(string? raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme is not ("http" or "https")) return;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
            && uri.Host.Equals("codalauncher.local", StringComparison.OrdinalIgnoreCase)) return;
        e.Cancel = true;
        OpenExternal(e.Uri);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        OpenExternal(e.Uri);
    }

    private void Send(object value)
    {
        if (Browser.CoreWebView2 is null) return;
        Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value, _json));
    }
}
