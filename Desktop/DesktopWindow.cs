using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System.Diagnostics;

namespace HowlingWhispers.CodaLauncher;

internal sealed class DesktopWindow : Window
{
    private readonly MinecraftAccount _account = new();
    private CancellationTokenSource? _signIn;
    private bool _accountBusy;
    private readonly TextBlock _accountStatus = Text("", 16);
    private readonly TextBlock _accountCode = Text("", 22);
    private readonly Button _signInButton = new() { Content = "SIGN IN WITH MICROSOFT" };
    private readonly Button _verifyButton = new() { Content = "VERIFY AGAIN" };
    private readonly Button _signOutButton = new() { Content = "SIGN OUT" };
    private readonly Button _cancelButton = new() { Content = "CANCEL SIGN-IN", IsVisible = false };
    private readonly CheckBox _offline = new() { Content = "Play offline (local worlds only)" };
    private readonly InstallService _installer = new();
    private readonly FeedService _feeds = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly LauncherSettings _settings;
    private readonly LogBuffer _logs = new();
    private readonly LauncherService _launcher;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _updates = new() { Interval = TimeSpan.FromMinutes(5) };
    private readonly TextBlock _status = Text("Coda is checking the clipboard...", 16);
    private readonly TextBlock _versions = Text("Minecraft 26.4 Snapshot 3", 14);
    private readonly TextBlock _news = Text("News is on its way.", 15);
    private readonly TextBlock _mods = Text("Checking your Minecraft profile...", 16);
    private readonly TextBox _log = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 400 };
    private readonly Button _play = new() { Content = "PLAY", FontSize = 20, Padding = new Thickness(30, 14), Background = Brush.Parse("#25576a") };
    private readonly Button _repair = new() { Content = "INSTALL / REPAIR" };
    private readonly Button _refresh = new() { Content = "REFRESH" };
    private readonly Button _update = new() { Content = "UPDATE AVAILABLE", IsVisible = false };
    private readonly TextBox _feedUrl;
    private bool _busy, _running, _refreshing, _checkingUpdate;
    private string? _updateUrl;
    private readonly Bitmap _portrait;

    public DesktopWindow()
    {
        Title = "CodaLauncher • Howling Whispers";
        Width = 1000; Height = 740; MinWidth = 650; MinHeight = 520;
        Background = Brush.Parse("#09151d");
        _settings = _settingsStore.Load();
        _feedUrl = new TextBox { Text = _settings.FeedUrl, MinWidth = 360 };
        _portrait = new Bitmap(Path.Combine(AppContext.BaseDirectory, "assets", "coda-headshot.png"));
        _launcher = new LauncherService(_logs, _ => Dispatcher.UIThread.Post(UpdateLog));
        _launcher.SessionStarted += _ => Dispatcher.UIThread.Post(() => { _running = true; SetControls(); _status.Text = "Minecraft is running. Coda is on standby."; });
        _launcher.SessionExited += (_, code) => Dispatcher.UIThread.Post(async () => {
            _running = false; SetControls();
            _status.Text = code == 0 ? "Minecraft closed. Ready when you are." : "Minecraft closed unexpectedly. Coda left the logs on the desk.";
            await RefreshAsync(false);
        });

        var root = new DockPanel { Margin = new Thickness(28) };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 22, Margin = new Thickness(0, 0, 0, 18) };
        heading.Children.Add(new Image { Source = _portrait, Width = 150, Height = 150, Stretch = Stretch.Uniform });
        var title = Stack(Text("HOWLING WHISPERS", 14), Text("CodaLauncher", 32), Text("Clipboard ready. Adventure pending.", 16), Text(Program.Version, 12));
        title.VerticalAlignment = VerticalAlignment.Center;
        heading.Children.Add(title);
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 0, 0, 16) };
        actions.Children.Add(_play); actions.Children.Add(_repair); actions.Children.Add(_refresh); actions.Children.Add(_update);
        DockPanel.SetDock(actions, Dock.Top); root.Children.Add(actions);
        var tabs = new TabControl();
        tabs.Items.Add(Tab("HOME", Stack(_status, _versions, Text("Coda's noticeboard", 22), _news)));
        var openMods = new Button { Content = "OPEN MODS FOLDER" };
        openMods.Click += (_, _) => OpenFolder(Path.Combine(AppPaths.MinecraftRoot, "mods"));
        tabs.Items.Add(Tab("MODS", Stack(Text("Your CML mods", 22), _mods, openMods)));
        _signInButton.Click += async (_, _) => await AccountActionAsync(false);
        _verifyButton.Click += async (_, _) => await AccountActionAsync(true);
        _cancelButton.Click += (_, _) => _signIn?.Cancel();
        _signOutButton.Click += async (_, _) => {
            if (_running || _busy) return;
            _signIn?.Cancel();
            try { await _account.SignOutAsync(_lifetime.Token); SetControls(); }
            catch (Exception ex) { _accountStatus.Text = ex.Message; }
        };
        _offline.IsChecked = _settings.OfflineMode;
        _offline.IsCheckedChanged += (_, _) => {
            if (_running || _accountBusy) return;
            _settings.OfflineMode = _offline.IsChecked == true;
            _settingsStore.Save(_settings); SetControls();
        };
        tabs.Items.Add(Tab("PROFILE", Stack(Text("Minecraft account", 22), _accountStatus, _accountCode,
            _signInButton, _verifyButton, _signOutButton, _cancelButton, _offline,
            Text("Microsoft handles your password. Verify ownership first; offline access lasts 30 days. Install Minecraft while online. Signing out keeps saved worlds.", 14))));
        SetControls();
        var save = new Button { Content = "SAVE SETTINGS" };
        save.Click += async (_, _) => {
            if (!Uri.TryCreate(_feedUrl.Text, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
            { _status.Text = "Use a valid http or https feed URL."; return; }
            _settings.FeedUrl = uri.ToString(); _settingsStore.Save(_settings); await RefreshAsync();
        };
        var openData = new Button { Content = "OPEN INSTALL FOLDER" };
        openData.Click += (_, _) => OpenFolder(AppPaths.InstallRoot);
        tabs.Items.Add(Tab("SETTINGS", Stack(Text("Launcher feed", 20), _feedUrl, save,
            Text("Minecraft requires Java 25 or newer on PATH. Profile handles Microsoft sign-in; CodaLoader handles Minecraft downloads.", 15),
            Text("Install folder: " + AppPaths.InstallRoot, 14), openData)));
        var copyLogs = new Button { Content = "COPY ALL LOGS" };
        var copyStatus = Text("", 13);
        copyLogs.Click += async (_, _) => {
            try {
                var clipboard = Clipboard;
                if (clipboard == null) throw new InvalidOperationException("Clipboard unavailable.");
                var lines = _logs.Snapshot();
                await clipboard.SetTextAsync(string.Join(Environment.NewLine, lines));
                copyStatus.Text = "Copied " + lines.Length + " log lines.";
            }
            catch (Exception ex) { copyStatus.Text = "Copy failed: " + ex.Message; }
        };
        tabs.Items.Add(Tab("LOGS", Stack(copyLogs, copyStatus, _log))); root.Children.Add(tabs); Content = root;
        _play.Click += async (_, _) => await PrepareAsync(true);
        _repair.Click += async (_, _) => await PrepareAsync(false);
        _refresh.Click += async (_, _) => await RefreshAsync();
        _update.Click += (_, _) => { if (_updateUrl != null && !_busy && !_running) Open(_updateUrl); };
        _updates.Tick += async (_, _) => await CheckUpdateAsync();
        Opened += async (_, _) => { if (!Program.SmokeUi) { _updates.Start(); await RefreshAsync(); } };
        Activated += async (_, _) => { if (!Program.SmokeUi) await CheckUpdateAsync(); };
        Closed += (_, _) => { _updates.Stop(); _lifetime.Cancel(); _portrait.Dispose(); };
    }

    private async Task AccountActionAsync(bool verifyOnly)
    {
        if (_accountBusy || _running || _busy) return;
        _accountBusy = true; _signIn = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); SetControls();
        string? result = null;
        try
        {
            if (verifyOnly) await _account.PrepareLaunchAsync(false, _signIn.Token);
            else await _account.SignInAsync(prompt => Dispatcher.UIThread.Post(() => {
                _accountCode.Text = "Enter " + prompt.Code + " at " + prompt.Url;
                Open(prompt.Url);
            }), _signIn.Token);
        }
        catch (OperationCanceledException) { result = "Sign-in cancelled or expired."; }
        catch (Exception ex) { result = ex.Message; }
        finally
        {
            _signIn.Dispose(); _signIn = null; _accountBusy = false; _accountCode.Text = ""; SetControls();
            if (result is not null) _accountStatus.Text = result;
        }
    }

    private async Task RefreshAsync(bool replaceStatus = true)
    {
        if (_refreshing || _busy || _lifetime.IsCancellationRequested) return;
        _refreshing = true; _refresh.IsEnabled = false;
        try
        {
            var feed = await _feeds.FetchAsync(_settings.FeedUrl, _lifetime.Token);
            _installer.CurrentFeedBase = new Uri(_settings.FeedUrl.TrimEnd('/') + "/");
            var state = await Task.Run(() => _installer.CheckManagedStateAsync(feed, _lifetime.Token));
            _versions.Text = $"Minecraft 26.4 Snapshot 3 • CodaLoader {state.InstalledLoaderVersion ?? "not installed"} • Latest {state.LatestLoaderVersion}";
            if (replaceStatus && !_running) _status.Text = state.Current ? "Everything is where it belongs. Ready to play." : "Coda has updates to file. PLAY prepares everything automatically.";
            _news.Text = string.Join("\n\n", feed.News.Take(8).Select(n => n.Title + "\n" + n.Text));
            ScanMods();
            await CheckUpdateAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Report("Check failed: " + ex.Message); ScanMods(); }
        finally { _refreshing = false; SetControls(); }
    }

    private async Task PrepareAsync(bool launch)
    {
        if (_busy || _running || _refreshing) return;
        _busy = true; SetControls();
        try
        {
            var identity = launch ? await _account.PrepareLaunchAsync(_settings.OfflineMode, _lifetime.Token) : null;
            if (launch && _settings.OfflineMode)
            {
                _launcher.Launch(AppPaths.LoaderRoot, identity!);
                return;
            }
            var feed = await _feeds.FetchAsync(_settings.FeedUrl, _lifetime.Token);
            _installer.CurrentFeedBase = new Uri(_settings.FeedUrl.TrimEnd('/') + "/");
            await Task.Run(() => _installer.InstallOrRepairAsync(feed,
                message => Dispatcher.UIThread.Post(() => Report(message)), _lifetime.Token));
            _settings.LoaderPath = AppPaths.LoaderRoot; _settingsStore.Save(_settings);
            ScanMods();
            if (launch) _launcher.Launch(AppPaths.LoaderRoot, identity!);
            else _status.Text = "Install ready. Coda has checked the essentials.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Report("Coda couldn't prepare Minecraft: " + ex.Message); }
        finally { _busy = false; SetControls(); }
    }

    private async Task CheckUpdateAsync()
    {
        if (_checkingUpdate || _lifetime.IsCancellationRequested) return;
        _checkingUpdate = true;
        try
        {
            var update = await DesktopUpdates.FindAsync(Program.Version, _lifetime.Token);
            _updateUrl = update?.Url; _update.IsVisible = update != null;
            if (update != null) _update.Content = "DOWNLOAD UPDATE " + update.Version;
        }
        catch (OperationCanceledException) { }
        catch { /* An unavailable update service does not block Play. */ }
        finally { _checkingUpdate = false; }
    }

    private void ScanMods()
    {
        var mods = new ModScanner().Scan(AppPaths.MinecraftRoot);
        _mods.Text = mods.Count == 0 ? "No CML mods installed yet. PLAY installs HW Essentials automatically."
            : string.Join("\n\n", mods.Select(m => $"{m.Name} {m.Version} • {m.Id}\n{m.FileName} • {(m.Valid ? "Ready" : m.Error)}"));
    }
    private void Report(string message) { _status.Text = message; _logs.Add(message); UpdateLog(); }
    private void UpdateLog() { _log.Text = string.Join("\n", _logs.Snapshot()); _log.CaretIndex = _log.Text.Length; }
    private void SetControls()
    {
        var account = _account.View;
        _accountStatus.Text = account.Status + "\n" + account.PlayerName + "\n" + account.Storage;
        _signInButton.IsEnabled = !_accountBusy && !_running && !_busy && account.Configured;
        _verifyButton.IsEnabled = _signInButton.IsEnabled && account.SignedIn;
        _signOutButton.IsEnabled = !_running && !_busy && account.SignedIn;
        _cancelButton.IsVisible = _accountBusy;
        _offline.IsEnabled = !_accountBusy && !_running && !_busy && (account.OfflineAvailable || _settings.OfflineMode);
        _repair.IsEnabled = !_busy && !_running && !_refreshing && !_accountBusy;
        _play.IsEnabled = _repair.IsEnabled && account.SignedIn && (!_settings.OfflineMode || account.OfflineAvailable);
        _refresh.IsEnabled = !_busy && !_refreshing; _update.IsEnabled = !_busy && !_running;
        _play.Content = _running ? "MINECRAFT IS RUNNING" : _busy ? "CODA IS PREPARING..." : _settings.OfflineMode ? "PLAY OFFLINE" : "PLAY";
    }
    private void OpenFolder(string path) { Directory.CreateDirectory(path); Open(path); }
    private void Open(string target)
    {
        try {
            var info = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { UseShellExecute = false };
            info.ArgumentList.Add(target); Process.Start(info);
        } catch (Exception ex) { Report("Could not open: " + ex.Message); }
    }
    private static TextBlock Text(string value, double size) => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap };
    private static StackPanel Stack(params Control[] controls) { var panel = new StackPanel { Spacing = 16, Margin = new Thickness(4, 18) }; foreach (var c in controls) panel.Children.Add(c); return panel; }
    private static TabItem Tab(string header, Control content) => new() { Header = header, Content = new ScrollViewer { Content = content } };
}
