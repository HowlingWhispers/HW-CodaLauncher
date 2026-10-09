using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace HowlingWhispers.CodaLauncher;

public partial class MainWindow : Window
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private readonly MinecraftAccount _account = new();
    private CancellationTokenSource? _accountSignIn;
    private bool _accountBusy;
    private readonly SettingsStore _settingsStore = new();
    private readonly LogBuffer _logs = new();
    private readonly FeedService _feeds = new();
    private readonly ModScanner _mods = new();
    private readonly LauncherService _launcher;
    private readonly InstallService _installer = new();
    private readonly NightlyBuildInstaller _nightly = new();
    private readonly CodaWolfNightlyInstaller _codaWolf = new();
    private LauncherSettings _settings = new();
    private LauncherFeed _lastFeed = new();
    private readonly List<NewsItem> _systemNews = [];
    private readonly SemaphoreSlim _installGate = new(1, 1);
    private bool _resumePlayOnReady;
    private volatile bool _gameRunning;
    private volatile int _gameProcessId;
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    private readonly CancellationTokenSource _windowLifetime = new();
    private LauncherUpdateInfo? _availableLauncherUpdate;
    private DateTime _lastLauncherUpdateCheck = DateTime.MinValue;
    private DateTime _rateLimitedLauncherUpdatesUntil = DateTime.MinValue;
    private bool _checkingLauncherUpdate;
    private bool _updateWindowOpen;
    private bool _uiReady;

    public MainWindow(bool resumePlayOnReady = false)
    {
        _resumePlayOnReady = resumePlayOnReady;
        InitializeComponent();
        _logs.LineAdded += line =>
        {
            if (!_uiReady) return;
            if (Dispatcher.CheckAccess()) Send(new { type = "log", line });
            else Dispatcher.BeginInvoke(() => Send(new { type = "log", line }));
        };
        _logs.Cleared += () =>
        {
            if (!_uiReady) return;
            if (Dispatcher.CheckAccess()) Send(new { type = "logsReset" });
            else Dispatcher.BeginInvoke(() => Send(new { type = "logsReset" }));
        };
        // LogBuffer now broadcasts every line. The launch callback must not
        // duplicate streamed stdout/stderr.
        _launcher = new LauncherService(_logs, _ => { });
        _launcher.SessionStarted += OnSessionStarted;
        _launcher.SessionExited += OnSessionExited;
        Loaded += OnLoaded;
        _updateTimer.Tick += async (_, _) => await CheckLauncherUpdateAsync();
        Activated += async (_, _) =>
        {
            if (_uiReady) await CheckLauncherUpdateAsync();
        };
        Closed += (_, _) =>
        {
            _updateTimer.Stop();
            _windowLifetime.Cancel();
        };
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
            if (App.IsSmokeTest) { Application.Current.Shutdown(1); return; }
            MessageBox.Show("CodaLauncher could not start its interface.\n\n" + ex.Message, "CodaLauncher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SendAccount() => Send(new { type = "account", account = _account.View, busy = _accountBusy, offline = _settings.OfflineMode });

    private async Task RunAccountAsync(bool verifyOnly)
    {
        if (LocalSingleplayer.Enabled)
        {
            Send(new { type = "accountMessage", message = "Microsoft sign-in is paused. Local singleplayer is available without Azure." });
            return;
        }
        if (_accountBusy || _gameRunning || _installGate.CurrentCount == 0) return;
        _accountBusy = true;
        _accountSignIn = CancellationTokenSource.CreateLinkedTokenSource(_windowLifetime.Token);
        SendAccount();
        try
        {
            if (verifyOnly) await _account.PrepareLaunchAsync(false, _accountSignIn.Token);
            else await _account.SignInAsync(prompt => Dispatcher.Invoke(() =>
            {
                Send(new { type = "accountCode", code = prompt.Code, url = prompt.Url });
                Process.Start(new ProcessStartInfo(prompt.Url) { UseShellExecute = true });
            }), _accountSignIn.Token);
            Send(new { type = "accountMessage", message = "Minecraft Java ownership verified. Coda has stamped the paperwork." });
        }
        catch (OperationCanceledException) { Send(new { type = "accountMessage", message = "Sign-in cancelled or expired. You can try again." }); }
        catch (Exception ex) { Send(new { type = "accountMessage", message = ex.Message }); }
        finally
        {
            _accountSignIn.Dispose(); _accountSignIn = null; _accountBusy = false;
            Send(new { type = "accountCode", code = "", url = "" });
            SendAccount();
        }
    }

    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? action = null;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            action = root.TryGetProperty("action", out var a) ? a.GetString() : null;

            switch (action)
            {
                case "ready":
                    if (App.IsSmokeTest) { Application.Current.Shutdown(0); return; }
                    _uiReady = true;
                    _updateTimer.Start();
                    if (!_resumePlayOnReady) _ = CheckLauncherUpdateAsync(force: true);
                    await SendState();
                    if (_resumePlayOnReady)
                    {
                        // A verified update carries Play through the restart once.
                        _resumePlayOnReady = false;
                        _logs.Add("Resuming Play after the CodaLauncher update.");
                        _ = Play();
                    }
                    break;
                case "copyAllLogs":
                    try
                    {
                        var lines = _logs.Snapshot();
                        Clipboard.SetText(string.Join(Environment.NewLine, lines));
                        Send(new { type = "copyLogsResult", ok = true, count = lines.Length });
                    }
                    catch (Exception copyError)
                    {
                        Send(new { type = "copyLogsResult", ok = false, message = copyError.Message });
                    }
                    break;
                case "refresh":
                    _ = CheckLauncherUpdateAsync(force: true);
                    await SendState();
                    break;
                case "updateLauncher":
                    await OpenLauncherUpdateAsync();
                    break;
                case "signInMicrosoft":
                    await RunAccountAsync(false);
                    break;
                case "verifyAccount":
                    await RunAccountAsync(true);
                    break;
                case "cancelSignIn":
                    _accountSignIn?.Cancel();
                    break;
                case "signOutAccount":
                    if (LocalSingleplayer.Enabled) throw new InvalidOperationException("Microsoft account controls are paused.");
                    if (_gameRunning || _installGate.CurrentCount == 0) throw new InvalidOperationException("Close Minecraft and finish preparation before signing out.");
                    _accountSignIn?.Cancel();
                    await _account.SignOutAsync(_windowLifetime.Token);
                    SendAccount();
                    break;
                case "setPlayMode":
                    if (LocalSingleplayer.Enabled) throw new InvalidOperationException("Local singleplayer is the only active play mode until Microsoft sign-in is enabled.");
                    if (_gameRunning || _accountBusy || _installGate.CurrentCount == 0) throw new InvalidOperationException("Finish the current session before changing play mode.");
                    _settings.OfflineMode = root.GetProperty("offline").GetBoolean();
                    _settingsStore.Save(_settings);
                    SendAccount();
                    break;
                case "saveSettings":
                    if (_gameRunning || _installGate.CurrentCount == 0) throw new InvalidOperationException("Close Minecraft and finish installation before changing Settings.");
                    if (!root.TryGetProperty("settings", out var s))
                        throw new InvalidOperationException("No settings were sent to save.");
                    var updated = s.Deserialize<LauncherSettings>(_json)
                        ?? throw new InvalidOperationException("Invalid settings payload.");
                    updated.OfflineMode = _settings.OfflineMode;
                    if (updated.UpdateChannel is not ("stable" or "nightly"))
                        throw new InvalidOperationException("Select Stable or Nightly for H.O.W.L. updates.");
                    if (updated.UpdateChannel == "nightly" && !updated.LocalTestMode)
                        throw new InvalidOperationException("Nightly requires Local Test Mode to protect your normal Minecraft installation.");
                    if (string.IsNullOrWhiteSpace(updated.FeedUrl))
                        updated.FeedUrl = "https://thehowlingwhispers.com/launcher";
                    _settingsStore.Save(updated);
                    _settings = updated;
                    _logs.Add("Launcher settings saved.");
                    Send(new { type = "settingsSaveResult", ok = true, message = "Settings saved." });
                    try { await SendState(); }
                    catch (Exception refreshError)
                    {
                        _logs.Add("Settings saved, but refreshing the launcher view failed: " + refreshError.Message);
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
                case "openModsFolder":
                    OpenModsFolder();
                    break;
                case "refreshMods":
                    SendMods();
                    break;
                case "installMod":
                    await ManageOptionalModAsync(root.GetProperty("id").GetString(), uninstall: false);
                    break;
                case "uninstallMod":
                    await ManageOptionalModAsync(root.GetProperty("id").GetString(), uninstall: true);
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
            if (action == "saveSettings")
                Send(new { type = "settingsSaveResult", ok = false, message = ex.Message });
            else
                Send(new { type = "error", message = ex.Message });
        }
    }

    private void SendMods()
    {
        var gameRoot = _settings.UpdateChannel == "nightly"
            ? NightlyBuildInstaller.GameRoot : AppPaths.MinecraftRoot;
        var found = _mods.Scan(gameRoot);
        Send(new
        {
            type = "modsState",
            mods = found,
            optionalMods = OptionalModCatalog.Build(found, gameRoot,
                _settings.UpdateChannel == "nightly"),
            modCount = found.Count(m => m.Valid),
            minecraftRoot = gameRoot
        });
    }

    private async Task SendState()
    {
        var loader = _settings.UpdateChannel == "nightly" && NightlyBuildInstaller.Installed
            ? NightlyBuildInstaller.LoaderRoot : LoaderLocator.Resolve(_settings.LoaderPath);
        var feedUrl = string.IsNullOrWhiteSpace(_settings.FeedUrl)
                ? "https://thehowlingwhispers.com/launcher"
                : _settings.FeedUrl;
        var feed = await _feeds.FetchAsync(feedUrl, CancellationToken.None);
        _lastFeed = feed;
        _installer.CurrentFeedBase = FeedBaseUri(feedUrl);

        EnsureCmlBaseCatalog(feed);

        var pack = feed.Packs.First(item =>
            item.Id.Equals("cml-base", StringComparison.OrdinalIgnoreCase));
        var resource = feed.ResourcePacks.First(item =>
            item.Id.Equals("cml-base-resources", StringComparison.OrdinalIgnoreCase));
        var managed = await _installer.CheckManagedStateAsync(feed, CancellationToken.None);
        bool activeNightly = _settings.UpdateChannel == "nightly";
        string installedRuntimeVersion = activeNightly
            ? (NightlyRuntimeInstaller.InstalledVersion ?? "Legacy / unknown")
            : (managed.InstalledLoaderVersion ?? "");
        bool activeRuntimeReady = activeNightly
            ? NightlyBuildInstaller.Installed : managed.LoaderInstalled;
        bool activeRuntimeCurrent = activeNightly
            ? activeRuntimeReady && installedRuntimeVersion == managed.LatestLoaderVersion
            : managed.LoaderCurrent;

        var loaderReady = activeRuntimeReady;
        var resourcePackReady = managed.ResourceCurrent;
        var basePackReady = managed.PackCurrent;
        var readyToPlay = managed.Current;
        var news = _systemNews.Concat(feed.News).ToList();
        // Count the active game JARs after asynchronous feed and install-state
        // checks, not before. Mods can appear during that work.
        var mods = _mods.Scan(_settings.UpdateChannel == "nightly"
            ? NightlyBuildInstaller.GameRoot : AppPaths.MinecraftRoot);

        Send(new
        {
            type = "state",
            data = new
            {
                launcherVersion = App.LauncherVersion,
                localSingleplayer = _settings.LocalTestMode,
                officialLauncher = !_settings.LocalTestMode,
                launcherUpdateVersion = _availableLauncherUpdate?.Version,
                loaderPath = loader ?? "",
                loaderReady,
                loaderCurrent = activeRuntimeCurrent,
                installedLoaderVersion = installedRuntimeVersion,
                latestLoaderVersion = managed.LatestLoaderVersion,
                basePackReady,
                managedInstalled = managed.Installed,
                managedCurrent = managed.Current,
                updatesAvailable = managed.UpdatesAvailable,
                readyToPlay,
                gameRunning = _gameRunning,
                gameProcessId = _gameRunning ? _gameProcessId : 0,
                installRoot = AppPaths.InstallRoot,
                minecraftRoot = _settings.UpdateChannel == "nightly" ? NightlyBuildInstaller.GameRoot : AppPaths.MinecraftRoot,
                activeChannel = _settings.UpdateChannel,
                nightlyInstalled = NightlyBuildInstaller.Installed,
                nightlyQuietInstalled = NightlyBuildInstaller.QuietInstalled,
                basePackVersion = pack.Version,
                packs = new[]
                {
                    new
                    {
                        id = pack.Id,
                        name = pack.Name,
                        description = pack.Description,
                        required = pack.Required,
                        installed = managed.PackInstalled,
                        current = managed.PackCurrent,
                        installedVersion = managed.PackInstalled ? pack.Version : "",
                        availableVersion = pack.Version,
                        source = "HOWL Pack Catalog",
                        dependencies = pack.ResourcePacks,
                        status = managed.PackCurrent
                            ? "Current"
                            : managed.PackInstalled ? "Update available" : "Required"
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
                        installed = managed.ResourceInstalled,
                        current = managed.ResourceCurrent,
                        installedVersion = managed.ResourceInstalled ? resource.Version : "",
                        availableVersion = resource.Version,
                        requiredBy = resource.RequiredBy,
                        source = "HW-CodaLoader Releases / launcher feed",
                        contents = new[] { "Title banner", "4 panorama scenes", "Menu music", "Splash/default presentation assets" },
                        status = managed.ResourceCurrent
                            ? "Current"
                            : managed.ResourceInstalled ? "Update available" : "Required"
                    }
                },
                modCount = mods.Count(m => m.Valid),
                mods,
                optionalMods = OptionalModCatalog.Build(mods,
                    _settings.UpdateChannel == "nightly"
                        ? NightlyBuildInstaller.GameRoot : AppPaths.MinecraftRoot,
                    _settings.UpdateChannel == "nightly"),
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
                account = _account.View,
                accountBusy = _accountBusy,
                profile = new { cmlAccount = "Not configured", minecraftOwnership = _account.View.SignedIn ? "Previously verified" : "Not verified", discord = "Not linked", avatar = "Coming later" }
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

            if (_settings.UpdateChannel == "nightly")
            {
                if (!_settings.LocalTestMode)
                    throw new InvalidOperationException("Nightly requires Local Test Mode.");
                await _nightly.InstallLatestAsync(message =>
                {
                    _logs.Add(message);
                    Dispatcher.Invoke(() => Send(new { type = "installStatus", busy = true, ok = true, message }));
                }, CancellationToken.None);
                SendMods();
                Send(new { type = "installStatus", busy = false, ok = true,
                    message = "Required H.O.W.L. Nightly runtime checked. Optional mods unchanged." });
                await SendState();
                return;
            }

            await _installer.InstallOrRepairAsync(
                _lastFeed,
                message =>
                {
                    _logs.Add(message);
                    if (message.StartsWith("H.O.W.L. ", StringComparison.Ordinal))
                        Dispatcher.Invoke(() => Send(new { type = "loaderUpdateStatus", message }));
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
            Send(new { type = "installStatus", busy = false, ok = true, message = "HOWL install ready." });
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
                Name = "HOWL Base",
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
                Name = "HOWL Base Resources",
                Version = "1",
                Required = true,
                Description = "Official title banner, panorama scenes, menu music, splashes and shared presentation assets.",
                Url = "/assets/CML-Base-Resources-v1.zip",
                Sha256 = "031f3b05d3efaf9b40436fedfee64cecfd92cf8d259edc3b637a633905231838",
                RequiredBy = ["cml-base"]
            });
        }
        foreach (var pack in feed.Packs)
            if (pack.Id.Equals("cml-base", StringComparison.OrdinalIgnoreCase)) pack.Name = "HOWL Base";
        foreach (var resource in feed.ResourcePacks)
            if (resource.Id.Equals("cml-base-resources", StringComparison.OrdinalIgnoreCase)) resource.Name = "HOWL Base Resources";
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

    private async Task Play()
    {
        if (_gameRunning)
        {
            Send(new
            {
                type = "sessionStatus",
                running = true,
                crashed = false,
                message = "Minecraft is already running. Coda is on standby."
            });
            return;
        }

        if (!await _installGate.WaitAsync(0))
        {
            Send(new
            {
                type = "launchStatus",
                ok = true,
                message = "Coda is already preparing the game."
            });
            return;
        }

        try
        {
            // The logs screen and COPY ALL now represent this launch attempt
            // and its current Minecraft session, not earlier stacked sessions.
            _logs.Clear();
            _logs.Add("Preparing Minecraft launch (" + _settings.UpdateChannel + ")...");
            if (_accountBusy) throw new InvalidOperationException("Finish account verification before launching Minecraft.");
            Send(new { type = "installStatus", busy = true, ok = true,
                message = "Checking CodaLauncher updates..." });
            LauncherUpdateInfo? playUpdate = null;
            try
            {
                using var checkTimeout = CancellationTokenSource.CreateLinkedTokenSource(_windowLifetime.Token);
                checkTimeout.CancelAfter(TimeSpan.FromSeconds(8));
                // Do not trust a stale background check: Play always checks the
                // published launcher before touching game components.
                playUpdate = await SelfUpdater.CheckAsync(App.LauncherVersion, checkTimeout.Token);
                _availableLauncherUpdate = playUpdate;
                SendLauncherUpdateNotice();
                _logs.Add(playUpdate is null
                    ? "CodaLauncher is current. Checking game components..."
                    : $"CodaLauncher {playUpdate.Version} is available; updating before Play.");
            }
            catch (OperationCanceledException) when (!_windowLifetime.IsCancellationRequested)
            {
                _logs.Add("Launcher update check timed out; continuing with the installed version.");
            }
            catch (HttpRequestException ex)
            {
                _logs.Add("Launcher update check unavailable; continuing with the installed version: " + ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logs.Add("Launcher update check failed; continuing with the installed version: " + ex.Message);
            }

            if (_windowLifetime.IsCancellationRequested) return;
            if (playUpdate is not null)
            {
                _updateWindowOpen = true;
                SendLauncherUpdateNotice();
                try
                {
                    var updater = new UpdateWindow(playUpdate, resumePlayAfterUpdate: true) { Owner = this };
                    updater.ShowDialog();
                    if (updater.RestartRequested)
                    {
                        Application.Current.Shutdown();
                        return;
                    }
                    if (!updater.ContinueWithoutUpdate)
                    {
                        _logs.Add("Launcher update postponed; Minecraft was not started.");
                        Send(new { type = "installStatus", busy = false, ok = true,
                            message = "Launcher update postponed. Press Play to try again." });
                        return;
                    }
                    _logs.Add("Player chose to play with the installed launcher.");
                }
                finally
                {
                    _updateWindowOpen = false;
                    SendLauncherUpdateNotice();
                }
            }
            var identity = _settings.LocalTestMode ? LocalSingleplayer.Identity() : null;
            if (_settings.UpdateChannel == "nightly")
            {
                if (!_settings.LocalTestMode)
                    throw new InvalidOperationException("H.O.W.L. Nightly currently supports local single-player only. Enable Local Test Mode in Settings.");
                Send(new { type = "installStatus", busy = true, ok = true,
                    message = "Checking required H.O.W.L. Nightly runtime. Optional mods unchanged..." });
                var nightRoot = await _nightly.InstallLatestAsync(message =>
                {
                    _logs.Add(message);
                    Dispatcher.Invoke(() => Send(new { type = "installStatus", busy = true, ok = true, message }));
                }, CancellationToken.None);
                SendMods();
                _logs.Add("Active H.O.W.L. Nightly runtime: v" +
                    (NightlyRuntimeInstaller.InstalledVersion ?? "legacy/unknown"));
                _logs.Add("Nightly world directory: " + NightlyBuildInstaller.GameRoot);
                _launcher.Launch(nightRoot, identity!, NightlyBuildInstaller.GameRoot);
                if (_settings.CloseAfterLaunch) Close();
                else
                {
                    // Game launch is already successful. A subsequent GitHub update-status
                    // refresh must never make the launcher report "launch preparation failed".
                    try { await SendState(); }
                    catch (Exception error) when (error is HttpRequestException
                            || error is TaskCanceledException)
                    {
                        var notice = "Minecraft Nightly launch started. GitHub status refresh is unavailable; " +
                                     "your installed game was not interrupted.";
                        _logs.Add(notice + " " + error.Message);
                        Send(new { type = "launchStatus", ok = true, message = notice });
                    }
                }
                return;
            }
            if (!_settings.LocalTestMode && _settings.OfflineMode && !LocalSingleplayer.Enabled)
            {
                var installed = LoaderLocator.Resolve(_settings.LoaderPath);
                if (!LoaderLocator.IsReady(installed)) throw new InvalidOperationException("Install Minecraft and CodaLoader while online before using offline play.");
                _launcher.Launch(installed!, identity);
                if (_settings.CloseAfterLaunch) Close();
                return;
            }
            EnsureCmlBaseCatalog(_lastFeed);
            Send(new
            {
                type = "installStatus",
                busy = true,
                ok = true,
                message = "Coda is checking the essentials..."
            });

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

            var loader = LoaderLocator.Resolve(_settings.LoaderPath);
            if (!LoaderLocator.IsReady(loader))
                throw new InvalidOperationException("Managed CodaLoader install is not ready.");

            Send(new
            {
                type = "installStatus",
                busy = true,
                ok = true,
                message = "Everything is where it belongs. Coda is opening Minecraft..."
            });

            if (_settings.LocalTestMode)
            {
                _launcher.Launch(loader!, identity!);
                if (_settings.CloseAfterLaunch) Close();
                else await SendState();
            }
            else
            {
                var profile = await OfficialMinecraftLauncher.InstallProfileAsync(
                    Path.Combine(loader!, "CodaLoader.jar"), AppPaths.MinecraftRoot, _windowLifetime.Token, basePackDirectory: AppPaths.CmlBaseResourcesRoot);
                var opened = OfficialMinecraftLauncher.TryOpenLauncher();
                var message = opened
                    ? "Official Minecraft Launcher opened. Select Howling Whispers | H.O.W.L., then press Play."
                    : "Official profile installed. Open Minecraft Launcher, select Howling Whispers | H.O.W.L., then press Play.";
                _logs.Add("H.O.W.L. official installation registered in " + profile);
                _logs.Add(message);
                Send(new { type = "installStatus", busy = false, ok = true, message });
                Send(new { type = "launchStatus", ok = true, message });
                await SendState();
            }
        }
        catch (Exception ex)
        {
            var message = FriendlyInstallError(ex);
            _logs.Add("Launch preparation failed: " + message);
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

    private void OnSessionStarted(int processId)
    {
        _gameRunning = true;
        _gameProcessId = processId;

        Dispatcher.Invoke(() =>
        {
            _logs.Add("Minecraft session active.");
            Send(new
            {
                type = "sessionStatus",
                running = true,
                crashed = false,
                message = "Minecraft is running. Coda is on standby."
            });
        });
    }

    private void OnSessionExited(int processId, int exitCode)
    {
        _gameRunning = false;
        _gameProcessId = 0;

        Dispatcher.BeginInvoke(async () =>
        {
            var crashed = exitCode != 0;
            var message = crashed
                ? "Minecraft closed unexpectedly. Coda left the logs on the desk."
                : "Minecraft closed. Ready when you are.";

            _logs.Add(crashed
                ? $"Minecraft session ended unexpectedly with exit code {exitCode}."
                : "Minecraft session closed normally.");

            Send(new
            {
                type = "sessionStatus",
                running = false,
                crashed,
                exitCode,
                message
            });

            await SendState();
        });
    }


    private async Task CheckLauncherUpdateAsync(bool force = false)
    {
        if (_checkingLauncherUpdate || _updateWindowOpen || _windowLifetime.IsCancellationRequested)
            return;
        if (DateTime.UtcNow < _rateLimitedLauncherUpdatesUntil)
            return;
        if (!force && DateTime.UtcNow - _lastLauncherUpdateCheck < TimeSpan.FromMinutes(1))
            return;

        _checkingLauncherUpdate = true;
        _lastLauncherUpdateCheck = DateTime.UtcNow;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_windowLifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            _availableLauncherUpdate = await SelfUpdater.CheckAsync(App.LauncherVersion, timeout.Token);
            if (!_windowLifetime.IsCancellationRequested) SendLauncherUpdateNotice();
        }
        catch (OperationCanceledException)
        {
            // An unavailable update service must not interrupt the launcher or game.
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Forbidden
                or System.Net.HttpStatusCode.TooManyRequests)
        {
            _rateLimitedLauncherUpdatesUntil = DateTime.UtcNow.AddHours(1);
            _logs.Add("GitHub API rate limit reached. Pausing automatic launcher update checks for one hour. " +
                      "Nightly mod updates use the public releases fallback independently.");
        }
        catch (Exception ex)
        {
            _logs.Add("Background launcher update check failed: " + ex.Message);
        }
        finally
        {
            _checkingLauncherUpdate = false;
        }
    }

    private void SendLauncherUpdateNotice() => Send(new
    {
        type = "launcherUpdate",
        version = _availableLauncherUpdate?.Version,
        busy = _updateWindowOpen
    });

    private async Task OpenLauncherUpdateAsync()
    {
        if (_updateWindowOpen || _gameRunning || _availableLauncherUpdate is null)
        {
            SendLauncherUpdateNotice();
            return;
        }
        if (!await _installGate.WaitAsync(0))
        {
            SendLauncherUpdateNotice();
            return;
        }

        _updateWindowOpen = true;
        SendLauncherUpdateNotice();
        try
        {
            var updater = new UpdateWindow(_availableLauncherUpdate) { Owner = this };
            updater.ShowDialog();
            if (updater.RestartRequested)
                Application.Current.Shutdown();
        }
        finally
        {
            _updateWindowOpen = false;
            _installGate.Release();
            if (!_windowLifetime.IsCancellationRequested) SendLauncherUpdateNotice();
        }
    }

    private async Task ManageOptionalModAsync(string? id, bool uninstall)
    {
        if (_gameRunning || _accountBusy)
            throw new InvalidOperationException("Close Minecraft before changing installed mods.");
        if (!await _installGate.WaitAsync(0))
            throw new InvalidOperationException("Another install/update operation is active.");
        string title = id switch
        {
            "buildcraft_cml" => "BuildCraft CML",
            "coda_wolf" => "Coda Wolf",
            "hw_essentials" => "HW Essentials",
            "quiet_underground" => "Quiet Underground",
            _ => throw new InvalidOperationException("Unknown optional mod.")
        };
        var root = _settings.UpdateChannel == "nightly"
            ? NightlyBuildInstaller.GameRoot : AppPaths.MinecraftRoot;
        void Report(string message)
        {
            _logs.Add(message);
            Dispatcher.Invoke(() => Send(new
            {
                type = "modActionStatus", busy = true, ok = true, message
            }));
        }

        try
        {
            Send(new { type = "modActionStatus", busy = true, ok = true,
                message = (uninstall ? "Uninstalling " : "Installing/updating ") + title + "..." });
            if (id is "coda_wolf" or "hw_essentials")
                throw new InvalidOperationException(title + " is a required H.O.W.L. component. "
                    + "It is automatically installed, repaired and updated with H.O.W.L. "
                    + "in Stable and Nightly. Manual removal is not supported.");
            if ((id is "buildcraft_cml" or "quiet_underground")
                && _settings.UpdateChannel != "nightly")
                throw new InvalidOperationException(title + " is currently offered only in Nightly.");
            if (uninstall)
            {
                switch (id)
                {
                    case "buildcraft_cml": NightlyBuildInstaller.UninstallBuildCraft(Report); break;
                    case "quiet_underground": NightlyBuildInstaller.UninstallQuiet(Report); break;
                    case "coda_wolf": _codaWolf.UninstallManaged(Report); break;
                    case "hw_essentials": ManagedMods.Uninstall(root, Report); break;
                }
            }
            else
            {
                switch (id)
                {
                    case "buildcraft_cml":
                        if (!NightlyBuildInstaller.Installed)
                            throw new InvalidOperationException("Install the required H.O.W.L. Nightly runtime first.");
                        await _nightly.InstallLatestAsync(Report, CancellationToken.None,
                            installBuildCraft: true);
                        break;
                    case "quiet_underground":
                        if (!NightlyBuildInstaller.Installed)
                            throw new InvalidOperationException("Install the required H.O.W.L. Nightly runtime first.");
                        await _nightly.InstallLatestAsync(Report, CancellationToken.None,
                            installQuiet: true);
                        break;
                    case "coda_wolf":
                        if (!NightlyBuildInstaller.Installed)
                            throw new InvalidOperationException("Install the required H.O.W.L. Nightly runtime first.");
                        await _codaWolf.InstallLatestAsync(Report, CancellationToken.None,
                            allowCachedFallback: false);
                        break;
                    case "hw_essentials":
                        var loader = _settings.UpdateChannel == "nightly"
                            ? NightlyBuildInstaller.LoaderRoot : LoaderLocator.Resolve(_settings.LoaderPath);
                        if (string.IsNullOrWhiteSpace(loader)
                            || !File.Exists(Path.Combine(loader, "CodaLoader.jar")))
                            throw new InvalidOperationException("Install the required H.O.W.L. runtime first.");
                        ManagedMods.Install(loader, root, Report);
                        break;
                }
            }
            SendMods();
            if (id == "quiet_underground")
                Send(new { type = "quietState", installed = NightlyBuildInstaller.QuietInstalled });
            Send(new { type = "modActionStatus", busy = false, ok = true,
                message = title + (uninstall ? " uninstalled." : " install/update completed.") });
        }
        catch (Exception ex)
        {
            var message = FriendlyInstallError(ex);
            _logs.Add("Optional mod action failed: " + message);
            Send(new { type = "modActionStatus", busy = false, ok = false, message });
            SendMods();
        }
        finally { _installGate.Release(); }
    }

    private void OpenModsFolder()
    {
        // The active game profile owns all mod JARs, never loader/run/mods.
        string game = _settings.UpdateChannel == "nightly"
            ? NightlyBuildInstaller.GameRoot : AppPaths.MinecraftRoot;
        string mods = Path.Combine(game, "mods");
        Directory.CreateDirectory(mods);
        Process.Start(new ProcessStartInfo("explorer.exe", mods) { UseShellExecute = true });
    }

    private void OpenLoaderFolder()
    {
        var loader = _settings.UpdateChannel == "nightly"
            ? NightlyBuildInstaller.LoaderRoot : LoaderLocator.Resolve(_settings.LoaderPath);
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
