using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace HowlingWhispers.CodaLauncher;

public partial class MainWindow : Window
{
    private const string Version = "0.4.3-loader-sync";
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private readonly SettingsStore _settingsStore = new();
    private readonly LogBuffer _logs = new();
    private readonly FeedService _feeds = new();
    private readonly ModScanner _mods = new();
    private readonly LauncherService _launcher;
    private readonly InstallService _installer = new();
    private LauncherSettings _settings = new();
    private LauncherFeed _lastFeed = new();
    private readonly List<NewsItem> _systemNews = [];
    private readonly SemaphoreSlim _installGate = new(1, 1);

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

            _ = CheckLauncherUpdateAsync();

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
                        if (string.IsNullOrWhiteSpace(_settings.FeedUrl))
                            _settings.FeedUrl = "https://thehowlingwhispers.com/launcher";
                        _settingsStore.Save(_settings);
                        _logs.Add("Launcher settings saved.");
                        await SendState();
                    }
                    break;
                case "install":
                    await InstallOrRepair();
                    break;
                case "installResourcePack":
                    await InstallResourcePack();
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

    private async Task CheckLauncherUpdateAsync()
    {
        LauncherUpdateInfo? update = null;
        try
        {
            update = await SelfUpdater.CheckAsync(Version, CancellationToken.None);
            if (update is null) return;

            _logs.Add($"Launcher update available: {update.Version}");
            Dispatcher.Invoke(() => Send(new
            {
                type = "launcherUpdate",
                available = true,
                version = update.Version
            }));

            await SelfUpdater.StageAndRestartAsync(
                update,
                message => _logs.Add(message),
                CancellationToken.None);

            Dispatcher.Invoke(Close);
        }
        catch (Exception ex)
        {
            _logs.Add("Launcher update check failed: " + ex.Message);
            if (update is not null)
            {
                _systemNews.RemoveAll(item => item.Id == "launcher-update-manual");
                _systemNews.Insert(0, new NewsItem
                {
                    Id = "launcher-update-manual",
                    Date = DateTime.Now.ToString("yyyy-MM-dd"),
                    Title = $"CodaLauncher {update.Version} needs a manual update",
                    Text = "Automatic updating could not finish. Open the release page to download the current launcher manually.",
                    Link = update.ReleaseUrl
                });
            }
        }
    }

    private async Task SendState()
    {
        var loader = LoaderLocator.Resolve(_settings.LoaderPath);
        var mods = _mods.Scan(AppPaths.MinecraftRoot);
        var feedUrl = string.IsNullOrWhiteSpace(_settings.FeedUrl)
                ? "https://thehowlingwhispers.com/launcher"
                : _settings.FeedUrl;
        var feed = await _feeds.FetchAsync(feedUrl, CancellationToken.None);
        _lastFeed = feed;
        _installer.CurrentFeedBase = FeedBaseUri(feedUrl);

        EnsureCmlBaseCatalog(feed);

        var loaderReady = LoaderLocator.IsReady(loader);
        var pack = feed.Packs.First(item =>
            item.Id.Equals("cml-base", StringComparison.OrdinalIgnoreCase));
        var resource = feed.ResourcePacks.First(item =>
            item.Id.Equals("cml-base-resources", StringComparison.OrdinalIgnoreCase));

        var resourcePackReady = _installer.CmlBaseResourcesReady(resource.Version);
        var basePackReady = _installer.CmlBasePackReady(pack.Version, resource.Version);
        var readyToPlay = loaderReady && basePackReady;
        var news = _systemNews.Concat(feed.News).ToList();

        Send(new
        {
            type = "state",
            data = new
            {
                launcherVersion = Version,
                loaderPath = loader ?? "",
                loaderReady,
                basePackReady,
                readyToPlay,
                installRoot = AppPaths.InstallRoot,
                minecraftRoot = AppPaths.MinecraftRoot,
                basePackVersion = pack.Version,
                packs = new[]
                {
                    new
                    {
                        id = pack.Id,
                        name = pack.Name,
                        description = pack.Description,
                        required = pack.Required,
                        installed = basePackReady,
                        installedVersion = basePackReady ? pack.Version : "",
                        availableVersion = pack.Version,
                        source = "CML Pack Catalog",
                        dependencies = pack.ResourcePacks,
                        status = basePackReady ? "Installed" : "Required"
                    }
                },
                resourcePacks = new[]
                {
                    new
                    {
                        id = resource.Id,
                        name = resource.Name,
                        description = resource.Description,
                        required = resource.Required,
                        installed = resourcePackReady,
                        installedVersion = resourcePackReady ? resource.Version : "",
                        availableVersion = resource.Version,
                        requiredBy = resource.RequiredBy,
                        source = "HW-CodaLoader Releases / launcher feed",
                        contents = new[] { "Title banner", "4 panorama scenes", "Menu music", "Splash/default presentation assets" },
                        status = resourcePackReady ? "Installed" : "Required"
                    }
                },
                modCount = mods.Count(m => m.Valid),
                mods,
                feed = new
                {
                    feed.Schema,
                    feed.ApiVersion,
                    feed.Project,
                    feed.Launcher,
                    feed.Codaloader,
                    feed.BasePack,
                    feed.Packs,
                    feed.ResourcePacks,
                    news,
                    feed.Online,
                    feed.Error
                },
                settings = _settings,
                logs = _logs.Snapshot(),
                profile = new { cmlAccount = "Not configured", minecraftOwnership = "Not verified", discord = "Not linked", avatar = "Coming later" }
            }
        });
    }

    private async Task InstallOrRepair()
    {
        if (!await _installGate.WaitAsync(0))
        {
            Send(new
            {
                type = "installStatus",
                busy = true,
                ok = true,
                message = "Install/repair is already running."
            });
            return;
        }

        try
        {
            Send(new { type = "installStatus", busy = true, ok = true, message = "Preparing install..." });

            await _installer.InstallOrRepairAsync(
                _lastFeed,
                message =>
                {
                    _logs.Add(message);
                    Dispatcher.Invoke(() => Send(new
                    {
                        type = "installStatus",
                        busy = true,
                        ok = true,
                        message
                    }));
                },
                CancellationToken.None);

            _settings.LoaderPath = AppPaths.LoaderRoot;
            _settingsStore.Save(_settings);
            Send(new { type = "installStatus", busy = false, ok = true, message = "CML install ready." });
            await SendState();
        }
        catch (Exception ex)
        {
            var message = FriendlyInstallError(ex);
            _logs.Add("Install/repair failed: " + message);
            Send(new { type = "installStatus", busy = false, ok = false, message });
            await SendState();
        }
        finally
        {
            _installGate.Release();
        }
    }

    private async Task InstallResourcePack()
    {
        if (!await _installGate.WaitAsync(0))
        {
            Send(new
            {
                type = "installStatus",
                busy = true,
                ok = true,
                message = "Install/repair is already running."
            });
            return;
        }

        try
        {
            Send(new
            {
                type = "installStatus",
                busy = true,
                ok = true,
                message = "Preparing Resourcepack install..."
            });

            await _installer.InstallCmlBaseResourcesOnlyAsync(
                _lastFeed,
                message =>
                {
                    _logs.Add(message);
                    Dispatcher.Invoke(() => Send(new
                    {
                        type = "installStatus",
                        busy = true,
                        ok = true,
                        message
                    }));
                },
                CancellationToken.None);

            Send(new
            {
                type = "installStatus",
                busy = false,
                ok = true,
                message = "CML Base Resources ready."
            });
            await SendState();
        }
        catch (Exception ex)
        {
            var message = FriendlyInstallError(ex);
            _logs.Add("Resourcepack install/repair failed: " + message);
            Send(new
            {
                type = "installStatus",
                busy = false,
                ok = false,
                message
            });
            await SendState();
        }
        finally
        {
            _installGate.Release();
        }
    }

    private static void EnsureCmlBaseCatalog(LauncherFeed feed)
    {
        if (!feed.Packs.Any(item =>
                item.Id.Equals("cml-base", StringComparison.OrdinalIgnoreCase)))
        {
            feed.Packs.Add(new PackCatalogInfo
            {
                Id = "cml-base",
                Name = "CML Base",
                Version = "1",
                Required = true,
                Description = "Required foundation pack for Howling Whispers Minecraft.",
                ResourcePacks = ["cml-base-resources"]
            });
        }

        if (!feed.ResourcePacks.Any(item =>
                item.Id.Equals("cml-base-resources", StringComparison.OrdinalIgnoreCase)))
        {
            feed.ResourcePacks.Add(new ResourcePackCatalogInfo
            {
                Id = "cml-base-resources",
                Name = "CML Base Resources",
                Version = "1",
                Required = true,
                Description = "Official title banner, panorama scenes, menu music, splashes and shared presentation assets.",
                Url = "/assets/CML-Base-Resources-v1.zip",
                Sha256 = "031f3b05d3efaf9b40436fedfee64cecfd92cf8d259edc3b637a633905231838",
                RequiredBy = ["cml-base"]
            });
        }
    }

    private static string FriendlyInstallError(Exception ex)
    {
        if (ex.Message.Contains("404", StringComparison.OrdinalIgnoreCase))
        {
            return "CML Base Resources v1 is not published yet. "
                + "The launcher is working, but the required resource pack is missing "
                + "from both HW-CodaLoader Releases and the Howling Whispers launcher feed.";
        }

        return ex.Message;
    }

    private static Uri? FeedBaseUri(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return Uri.TryCreate(raw.TrimEnd('/') + "/", UriKind.Absolute, out var uri) ? uri : null;
    }

    private Task Play()
    {
        var loader = LoaderLocator.Resolve(_settings.LoaderPath);
        EnsureCmlBaseCatalog(_lastFeed);
        var pack = _lastFeed.Packs.First(item =>
            item.Id.Equals("cml-base", StringComparison.OrdinalIgnoreCase));
        var resource = _lastFeed.ResourcePacks.First(item =>
            item.Id.Equals("cml-base-resources", StringComparison.OrdinalIgnoreCase));

        if (!LoaderLocator.IsReady(loader)
            || !_installer.CmlBasePackReady(pack.Version, resource.Version))
        {
            Send(new { type = "launchStatus", ok = false, message = "Install/repair CML Base and its required resource pack first." });
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
