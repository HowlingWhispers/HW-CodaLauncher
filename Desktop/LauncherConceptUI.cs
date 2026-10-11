using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Controls.Shapes;

namespace HowlingWhispers.CodaLauncher;

/// <summary>
/// A real Avalonia adaptation of the H.O.W.L. Launcher concept.
/// The shell owns presentation only: Play, repair, updates and identity remain
/// connected to DesktopWindow's existing services. No simulated verification.
/// </summary>
internal sealed class LauncherConceptUI
{
    private const string Ink = "#e7f0f6";
    private const string Muted = "#93abbc";
    private const string Cyan = "#7ed8e6";
    private const string Panel = "#10212e";
    private const string Line = "#273b4d";

    private readonly ContentControl _page = new();
    private readonly TextBlock _pageTitle = Label("Home", 22, Ink, true);
    private readonly TextBlock _heroTitle = Label("Checking the installation", 28, Ink, true);
    private readonly TextBlock _heroDetail = Label("Coda is checking the local profile and release information.", 14, Muted);
    private readonly TextBlock _moonText = Label("CHECK", 15, Cyan, true);
    private readonly TextBlock _trust = Label("Signed manifest verification is not yet active.", 13, "#e0a945");
    private readonly TextBlock _loader = Label("H.O.W.L. status pending", 13, Muted);
    private readonly TextBlock _profile = Label("Stable profile", 13, Muted);
    private readonly Dictionary<string, Control> _pages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> _nav = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action _refreshWorlds;
    internal Control Root { get; }

    internal LauncherConceptUI(
        Bitmap portrait,
        Button play, Button repair, Button refresh, Button update,
        Control legacyHome, Control library, Control worlds, Control settings,
        Control logs, Control account, Action refreshWorlds)
    {
        _refreshWorlds = refreshWorlds;
        var hero = BuildHero(portrait, play, repair, refresh, update);
        var lower = new StackPanel { Spacing = 14 };
        lower.Children.Add(Card(Vertical(
            Label("Activity and news", 18, Ink, true),
            Label("Information from the installed launcher and its feed.", 12, Muted),
            legacyHome)));
        lower.Children.Add(Card(Vertical(
            Label("Safety", 18, Ink, true),
            Label("Checks actually available in this build.", 12, Muted),
            SafetyRow("Distribution trust", _trust),
            SafetyRow("Installed loader", _loader),
            SafetyRow("Profile", _profile),
            SafetyRow("Your own files", Label("Never silently replaced or deleted", 13, Cyan)))));

        var home = new StackPanel { Spacing = 20 };
        home.Children.Add(hero);
        home.Children.Add(lower);
        AddPage("Home", new ScrollViewer { Content = home });
        AddPage("Library", library);
        AddPage("Worlds", worlds);
        AddPage("Settings", settings);
        AddPage("Logs", logs);
        AddPage("Profile", account);

        var sidebar = new DockPanel
        {
            Width = 216,
            Background = Brush.Parse("#0e1923"),
            LastChildFill = true
        };
        var accountButton = NavButton("♙  Profile / Account");
        accountButton.Click += (_, _) => Navigate("Profile");
        DockPanel.SetDock(accountButton, Dock.Bottom);
        sidebar.Children.Add(new Border
        {
            Child = accountButton,
            Padding = new Thickness(9, 12),
            BorderBrush = Brush.Parse(Line),
            BorderThickness = new Thickness(0, 1, 0, 0)
        });

        var sideContents = new StackPanel { Spacing = 10, Margin = new Thickness(14, 22) };
        sideContents.Children.Add(Label("◕  H.O.W.L.", 24, Cyan, true));
        sideContents.Children.Add(Label("HOWLING WHISPERS", 10, Muted, true));
        sideContents.Children.Add(new Border { Height = 14 });

        sideContents.Children.Add(Label("RELEASE CHANNEL", 10, Muted, true));
        var channels = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 5 };
        var stable = new Button
        {
            Content = "Stable", Background = Brush.Parse("#23495a"),
            Foreground = Brush.Parse(Ink), CornerRadius = new CornerRadius(8),
            FontWeight = FontWeight.SemiBold
        };
        var nightly = new Button
        {
            Content = "Nightly", IsEnabled = false,
            Background = Brush.Parse("#172b3c"), Foreground = Brush.Parse(Muted),
            CornerRadius = new CornerRadius(8)
        };
        ToolTip.SetTip(nightly, "Nightly isolation is not wired into the portable launcher yet. Stable remains active.");
        Grid.SetColumn(nightly, 1);
        channels.Children.Add(stable);
        channels.Children.Add(nightly);
        sideContents.Children.Add(channels);
        sideContents.Children.Add(Label("Stable uses your existing saves. Nightly is unavailable here until it is fully isolated.", 11, Muted));
        sideContents.Children.Add(new Border { Height = 12 });
        sideContents.Children.Add(Label("LAUNCHER", 10, Muted, true));
        foreach (var name in new[] { "Home", "Library", "Worlds", "Settings", "Logs" })
        {
            var nav = NavButton(name);
            nav.Click += (_, _) => Navigate(name);
            _nav.Add(name, nav);
            sideContents.Children.Add(nav);
        }
        sidebar.Children.Add(sideContents);

        var titlebar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(24, 15, 24, 12)
        };
        titlebar.Children.Add(_pageTitle);
        var phase = Label("CodaLauncher  •  native desktop", 12, Muted);
        phase.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(phase, 1);
        titlebar.Children.Add(phase);

        var main = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Background = Brush.Parse(Panel)
        };
        main.Children.Add(titlebar);
        var contentFrame = new Border { Padding = new Thickness(24, 5, 24, 22), Child = _page };
        Grid.SetRow(contentFrame, 1);
        main.Children.Add(contentFrame);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("216,*") };
        columns.Children.Add(sidebar);
        Grid.SetColumn(main, 1);
        columns.Children.Add(main);

        Root = new Border
        {
            Background = Brush.Parse("#0b1824"),
            BorderBrush = Brush.Parse(Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = columns
        };
        Navigate("Home");
    }

    private Border BuildHero(Bitmap portrait, Button play, Button repair, Button refresh, Button update)
    {
        play.Background = Brush.Parse(Cyan);
        play.Foreground = Brush.Parse("#06222a");
        play.CornerRadius = new CornerRadius(10);
        play.FontWeight = FontWeight.Bold;
        play.Padding = new Thickness(20, 12);
        repair.Background = Brush.Parse("#23495a");
        refresh.Background = Brush.Parse("#23495a");
        update.Background = Brush.Parse("#23495a");
        foreach (var button in new[] { repair, refresh, update })
        {
            button.Foreground = Brush.Parse(Ink);
            button.CornerRadius = new CornerRadius(8);
        }

        var moon = new Grid { Width = 124, Height = 124, HorizontalAlignment = HorizontalAlignment.Center };
        moon.Children.Add(new Ellipse
        {
            Stroke = Brush.Parse(Cyan), StrokeThickness = 3,
            Fill = Brush.Parse("#172b3c")
        });
        var moonCenter = Vertical(
            Label("☾", 46, "#eefcff", true), _moonText);
        moonCenter.HorizontalAlignment = HorizontalAlignment.Center;
        moonCenter.VerticalAlignment = VerticalAlignment.Center;
        moon.Children.Add(moonCenter);

        var actions = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var button in new[] { play, repair, refresh, update })
        {
            button.Margin = new Thickness(0, 0, 10, 10);
            actions.Children.Add(button);
        }
        var content = Vertical(
            Label("H.O.W.L.  /  MINECRAFT", 12, Cyan, true),
            _heroTitle,
            _heroDetail,
            actions,
            Label("Only an authenticated signed manifest can mark executable content verified.", 11, Muted));

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*"), ColumnSpacing = 22 };
        body.Children.Add(moon);
        Grid.SetColumn(content, 1);
        body.Children.Add(content);

        var identity = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        identity.Children.Add(new Image
        {
            Source = portrait, Width = 36, Height = 36,
            Stretch = Stretch.Uniform
        });
        identity.Children.Add(Label("Coda is keeping an eye on the install.", 12, Muted));
        content.Children.Insert(1, identity);

        return new Border
        {
            Background = Brush.Parse("#0b1824"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24),
            Child = body
        };
    }

    internal void ShowManaged(ManagedInstallStatus state)
    {
        _profile.Text = "Stable  •  Minecraft 26.4 Snapshot 3";
        _loader.Text = state.InstalledLoaderVersion is null
            ? "Not installed"
            : "Installed " + state.InstalledLoaderVersion + (state.LoaderCurrent ? " (current version)" : " (update available)");
        if (state.Current)
        {
            _moonText.Text = "LOCAL";
            _heroTitle.Text = "Ready to play";
            _heroDetail.Text = "The installed version matches the release information. Signed distribution verification is not implemented yet.";
        }
        else
        {
            _moonText.Text = "CHECK";
            _heroTitle.Text = "Preparation required";
            _heroDetail.Text = "Play will install or repair the H.O.W.L. runtime and required base resources.";
        }
    }

    internal void ShowMessage(string message)
    {
        _heroDetail.Text = message;
    }

    internal void ShowGameRunning()
    {
        _moonText.Text = "PLAY";
        _heroTitle.Text = "Minecraft is running";
        _heroDetail.Text = "Your session is active. Coda is watching the logs.";
    }

    private void AddPage(string name, Control page) => _pages.Add(name, page);

    private void Navigate(string name)
    {
        if (!_pages.TryGetValue(name, out var content)) return;
        if (name == "Worlds") _refreshWorlds();
        _pageTitle.Text = name;
        _page.Content = content;
        foreach (var (key, button) in _nav)
        {
            button.Background = Brush.Parse(key == name ? "#214957" : "#0e1923");
            button.Foreground = Brush.Parse(key == name ? Ink : Muted);
        }
    }

    private static TextBlock Label(string text, double size, string color, bool bold = false) =>
        new()
        {
            Text = text, FontSize = size,
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            Foreground = Brush.Parse(color),
            TextWrapping = TextWrapping.Wrap
        };

    private static Button NavButton(string title) => new()
    {
        Content = title,
        FontSize = 14,
        FontWeight = FontWeight.SemiBold,
        Background = Brush.Parse("#0e1923"),
        Foreground = Brush.Parse(Muted),
        BorderThickness = new Thickness(0),
        Padding = new Thickness(12, 11),
        HorizontalContentAlignment = HorizontalAlignment.Left,
        CornerRadius = new CornerRadius(8)
    };

    private static StackPanel Vertical(params Control[] controls)
    {
        var panel = new StackPanel { Spacing = 12 };
        foreach (var child in controls) panel.Children.Add(child);
        return panel;
    }

    private static Border Card(Control content) => new()
    {
        Background = Brush.Parse("#142938"),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(18),
        Child = content
    };

    private static Control SafetyRow(string title, Control detail)
    {
        var group = Vertical(Label(title, 14, Ink, true), detail);
        group.Spacing = 2;
        return new Border
        {
            BorderBrush = Brush.Parse(Line),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 10, 0, 4),
            Child = group
        };
    }
}
