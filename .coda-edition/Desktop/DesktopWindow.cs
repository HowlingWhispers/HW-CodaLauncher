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
    private readonly CheckBox _offline = new() { Content = "Verified offline (Microsoft paused)" };
    private readonly CheckBox _localTest = new() { Content = "Local Test Mode (no Microsoft sign-in, no online services)" };
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
    private readonly Button _play = new() { Content = "▶  Play", FontSize = 22, Padding = new Thickness(34, 16), Background = Brush.Parse("#ffcd7e"), Foreground = Brush.Parse("#30212a"), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _repair = new() { Content = "Install / repair" };
    private readonly Button _refresh = new() { Content = "↻ Check for updates" };
    private readonly Button _update = new() { Content = "UPDATE AVAILABLE", IsVisible = false };
    private readonly TextBox _feedUrl;
    private bool _busy, _running, _refreshing, _checkingUpdate;
    private string? _updateUrl;
    private readonly Bitmap _portrait;

    public DesktopWindow()
    {
        Title = "CodaLauncher · Coda Edition";
        Width = 1240; Height = 850; MinWidth = 940; MinHeight = 650;
        Background = Brush.Parse("#211b24");
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

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("195,*") };
        var sidebar = new DockPanel { Background = Brush.Parse("#2a222d"), Margin = new Thickness(0) };
        var brand = Stack(new Image { Source = _portrait, Width = 100, Height = 100, Stretch = Stretch.Uniform }, Serif("CODA", 31), Text("Howling Whispers", 12), Text("THE CODA EDITION", 9));
        brand.Margin = new Thickness(26, 30, 18, 30);
        DockPanel.SetDock(brand, Dock.Top); sidebar.Children.Add(brand);
        var note = Card(Stack(Text("You bring the adventure.\nI bring the clipboard. ♡", 17)), "#e8c68f");
        ((TextBlock)((StackPanel)note.Child!).Children[0]).Foreground = Brush.Parse("#49303a");
        note.Margin = new Thickness(18, 24);
        DockPanel.SetDock(note, Dock.Bottom); sidebar.Children.Add(note);
        var nav = new StackPanel { Spacing = 8, Margin = new Thickness(14, 0) };
        sidebar.Children.Add(nav); root.Children.Add(sidebar);
        var content = new ContentControl(); Grid.SetColumn(content, 1);
        content.Margin = new Thickness(34, 28); root.Children.Add(content);
        var pages = new Dictionary<string, Control>();
        var navButtons = new Dictionary<string, Button>();
        void ShowPage(string key) {
            content.Content = new ScrollViewer { Content = pages[key] };
            foreach (var entry in navButtons) {
                entry.Value.Background = Brush.Parse(entry.Key == key ? "#654a38" : "#2a222d");
                entry.Value.Foreground = Brush.Parse(entry.Key == key ? "#ffdb9a" : "#d5bdc7");
            }
        }
        foreach (var (key, label) in new[] { ("Home", "⌂   Home"), ("My Game", "◇   My Game"), ("Settings", "⚙   Settings"), ("Help", "?   Help"), ("Profile", "◉   Your profile") }) {
            var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(15, 13), FontSize = 15, Background = Brush.Parse("#2a222d") };
            button.Click += (_, _) => ShowPage(key); nav.Children.Add(button); navButtons[key] = button;
        }
        var home = Stack(Text("A LITTLE HOWL. A LOT OF HEART.", 11), Serif("Welcome back. Adventure?", 40), Text("Good to see you. Let’s get you out there.", 15));
        var hero = new Grid { ColumnDefinitions = new ColumnDefinitions("1.1*,1*"), MinHeight = 375 };
        var heroCopy = Stack(Text("H.O.W.L.  /  MINECRAFT JAVA", 11), Serif("Your next adventure\nstarts here.", 32), _status, _play,
            Text("Opens the official Minecraft Launcher. Local Test Mode starts the game directly.", 12));
        heroCopy.Margin = new Thickness(12, 16, 16, 16); hero.Children.Add(heroCopy);
        var speech = Text("Clipboard ready.\nLet’s check the supplies. ♡", 17);
        var boop = new Button { Background = Brushes.Transparent, Padding = new Thickness(0), Content = new Image { Source = _portrait, Width = 290, Height = 290, Stretch = Stretch.Uniform } };
        var quietCoda = new CheckBox { Content = "A quieter desk · fewer spontaneous Coda remarks" };
        var antics = new[] { "That was a boop. It goes in the report.", "I packed enthusiasm. Possibly too much.", "The desk is tidy. Please don’t open that drawer.", "I’m supervising. The ears are part of the uniform." }; var antic = 0;
        boop.Click += (_, _) => speech.Text = quietCoda.IsChecked == true ? "Good to see you." : antics[antic++ % antics.Length];
        var companion = Stack(speech, boop, Text("Coda · Keeper of the clipboard", 11));
        Grid.SetColumn(companion, 1); hero.Children.Add(companion);
        home.Children.Add(Card(hero, "#382c34"));
        var details = new Expander { Header = "Game details", Content = Stack(_versions, _repair, _refresh, Text("Separate Coda Edition game folder: " + AppPaths.InstallRoot, 12)) };
        home.Children.Add(details); home.Children.Add(Serif("From Coda’s desk", 26)); home.Children.Add(Card(Stack(_news), "#322731"));
        pages["Home"] = home;
        var openMods = new Button { Content = "Open mods folder" };
        openMods.Click += (_, _) => OpenFolder(Path.Combine(AppPaths.MinecraftRoot, "mods"));
        pages["My Game"] = Stack(Text("CODA’S MOD DRAWER", 11), Serif("Everything in its little place.", 32), Text("Required H.O.W.L. content is installed automatically before launch.", 15), Card(Stack(_mods, openMods), "#322731"));
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
        _localTest.IsChecked = _settings.LocalTestMode;
        _localTest.IsCheckedChanged += (_, _) => {
            if (_running || _busy) { _localTest.IsChecked = _settings.LocalTestMode; return; }
            _settings.LocalTestMode = _localTest.IsChecked == true;
            _settingsStore.Save(_settings);
            SetControls();
        };
        if (LocalSingleplayer.Enabled) _offline.IsEnabled = false;
        _offline.IsCheckedChanged += (_, _) => {
            if (_running || _accountBusy) return;
            _settings.OfflineMode = _offline.IsChecked == true;
            _settingsStore.Save(_settings); SetControls();
        };
        pages["Profile"] = Stack(Text("Minecraft account", 22), _accountStatus, _accountCode,
            _signInButton, _verifyButton, _signOutButton, _cancelButton, _offline,
            Text("Microsoft sign-in is paused for this development build. Local singleplayer does not verify ownership or enable online services.", 14));
        SetControls();
        var save = new Button { Content = "SAVE SETTINGS" };
        var saveStatus = Text("", 14);
        save.Click += async (_, _) => {
            if (!save.IsEnabled) return;
            if (!Uri.TryCreate(_feedUrl.Text, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
            { saveStatus.Text = "Use a valid http or https feed URL."; return; }
            save.IsEnabled = false;
            saveStatus.Text = "Saving...";
            try {
                _settings.FeedUrl = uri.ToString();
                _settingsStore.Save(_settings);
                saveStatus.Text = "Settings saved. Coda filed the paperwork.";
            }
            catch (Exception ex) { saveStatus.Text = "Could not save Settings: " + ex.Message; return; }
            finally { save.IsEnabled = true; }
            await RefreshAsync();
        };
        var openData = new Button { Content = "OPEN INSTALL FOLDER" };
        openData.Click += (_, _) => OpenFolder(AppPaths.InstallRoot);
        pages["Settings"] = Stack(Text("CODA’S CLIPBOARD", 11), Serif("The useful little knobs.", 32), quietCoda, Text("Launcher feed", 20), _feedUrl, save, saveStatus,
            Text("Play normally through the official Minecraft Launcher. Enable local testing only for unverified singleplayer development.", 14), _localTest,
            Text("Minecraft requires Java 25 or newer on PATH. Profile handles Microsoft sign-in; H.O.W.L. handles Minecraft downloads.", 15),
            Text("Install folder: " + AppPaths.InstallRoot, 14), openData);
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
        pages["Help"] = Stack(Text("CODA’S PAPER TRAIL", 11), Serif("A paw with that?", 32), Text("Something went sideways? Copy the logs when reporting a problem. Coda kept the paperwork.", 15), copyLogs, copyStatus, _log);
        Content = root; ShowPage("Home");
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
        if (LocalSingleplayer.Enabled || _accountBusy || _running || _busy) return;
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
            _versions.Text = $"Minecraft 26.4 Snapshot 3 • H.O.W.L. {state.InstalledLoaderVersion ?? "not installed"} • Latest {state.LatestLoaderVersion}";
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
            var identity = launch && _settings.LocalTestMode ? LocalSingleplayer.Identity() : null;
            if (launch && _settings.OfflineMode && !LocalSingleplayer.Enabled)
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
            if (!launch) _status.Text = "Install ready. Coda has checked the essentials.";
            else if (_settings.LocalTestMode) _launcher.Launch(AppPaths.LoaderRoot, identity!);
            else {
                await OfficialMinecraftLauncher.InstallProfileAsync(
                    Path.Combine(AppPaths.LoaderRoot, "CodaLoader.jar"), AppPaths.MinecraftRoot, _lifetime.Token, basePackDirectory: AppPaths.CmlBaseResourcesRoot);
                var opened = OfficialMinecraftLauncher.TryOpenLauncher();
                _status.Text = opened
                    ? "Minecraft Launcher opened. Select Howling Whispers | H.O.W.L. · Coda Edition and press Play."
                    : "Open Minecraft Launcher, select Howling Whispers | H.O.W.L. · Coda Edition, then press Play.";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Report("Coda couldn't prepare Minecraft: " + ex.Message); }
        finally { _busy = false; SetControls(); }
    }

    private async Task CheckUpdateAsync()
    {
        if (!Program.AllowLauncherUpdates || _checkingUpdate || _lifetime.IsCancellationRequested) return;
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
        _mods.Text = mods.Count == 0 ? "No HOWL mods installed yet. PLAY installs HW Essentials automatically."
            : string.Join("\n\n", mods.Select(m =>
                $"{m.Name} • {m.Id}\nManifest v{m.Version} • {m.FileName}" +
                (m.ReleaseStatus == "Verified" ? $"\nRelease: {m.ReleaseTag} • SHA-256 verified locally" :
                 m.ReleaseStatus == "Modified" ? "\nManaged JAR was modified: release identity not verified" :
                 m.ReleaseStatus == "Untracked" ? "\nNo verified Nightly release marker" : "") +
                $"\n{(m.Valid ? "Recognized (not gameplay verified)" : m.Error)}"));
    }
    private void Report(string message) { _status.Text = message; _logs.Add(message); UpdateLog(); }
    private void UpdateLog() { _log.Text = string.Join("\n", _logs.Snapshot()); _log.CaretIndex = _log.Text.Length; }
    private void SetControls()
    {
        _localTest.IsEnabled = !_busy && !_running && !_refreshing;
        var account = _account.View;
        _accountStatus.Text = account.Status + "\n" + account.PlayerName + "\n" + account.Storage;
        _signInButton.IsEnabled = !_accountBusy && !_running && !_busy && account.Configured && !LocalSingleplayer.Enabled;
        _verifyButton.IsEnabled = _signInButton.IsEnabled && account.SignedIn;
        _signOutButton.IsEnabled = !_running && !_busy && account.SignedIn && !LocalSingleplayer.Enabled;
        _cancelButton.IsVisible = _accountBusy;
        _offline.IsEnabled = !LocalSingleplayer.Enabled && !_accountBusy && !_running && !_busy && (account.OfflineAvailable || _settings.OfflineMode);
        _repair.IsEnabled = !_busy && !_running && !_refreshing && !_accountBusy;
        _play.IsEnabled = _repair.IsEnabled;
        _refresh.IsEnabled = !_busy && !_refreshing; _update.IsEnabled = !_busy && !_running;
        _play.Content = _running ? "Adventure in progress" : _busy ? "Packing for adventure…" : _settings.LocalTestMode ? "▶  Play local (test)" : "▶  Play";
    }
    private void OpenFolder(string path) { Directory.CreateDirectory(path); Open(path); }
    private void Open(string target)
    {
        try {
            var info = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { UseShellExecute = false };
            info.ArgumentList.Add(target); Process.Start(info);
        } catch (Exception ex) { Report("Could not open: " + ex.Message); }
    }
    private static TextBlock Text(string value, double size) => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#eadacb") };
    private static TextBlock Serif(string value, double size) => new() { Text = value, FontSize = size, FontFamily = new FontFamily("Georgia,DejaVu Serif"), FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#ffe3b5"), TextWrapping = TextWrapping.Wrap };
    private static Border Card(Control content, string background) => new() { Child = content, Background = Brush.Parse(background), BorderBrush = Brush.Parse("#79574a"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(22, 16) };
    private static StackPanel Stack(params Control[] controls) { var panel = new StackPanel { Spacing = 16, Margin = new Thickness(4, 18) }; foreach (var c in controls) panel.Children.Add(c); return panel; }
    private static TabItem Tab(string header, Control content) => new() { Header = header, Content = new ScrollViewer { Content = content } };
}
