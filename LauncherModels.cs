namespace HowlingWhispers.CodaLauncher;

public sealed class LauncherSettings
{
    public string LoaderPath { get; set; } = "";
    public string FeedUrl { get; set; } = "";
    public bool CloseAfterLaunch { get; set; }
}

public sealed class CmlBasePackInfo
{
    public bool Required { get; set; } = true;
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

public sealed record ModInfo(string FileName, string Id, string Name, string Version, bool Valid, string? Error);

public sealed class LauncherFeed
{
    public int Schema { get; set; } = 1;
    public int ApiVersion { get; set; } = 1;
    public string Project { get; set; } = "Howling Whispers";
    public LauncherFeedInfo Launcher { get; set; } = new();
    public CodaLoaderFeedInfo Codaloader { get; set; } = new();
    public CmlBasePackInfo BasePack { get; set; } = new();
    public List<NewsItem> News { get; set; } = [];
    public bool Online { get; set; }
    public string? Error { get; set; }
}

public sealed class LauncherFeedInfo
{
    public string Name { get; set; } = "CodaLauncher";
    public string Phase { get; set; } = "prototype";
    public bool Available { get; set; }
}

public sealed class CodaLoaderFeedInfo
{
    public string Version { get; set; } = "unknown";
    public string Minecraft { get; set; } = "26.4-snapshot-3";
    public string ReleasePage { get; set; } = "https://github.com/HowlingWhispers/HW-CodaLoader/releases";
}

public sealed class NewsItem
{
    public string Id { get; set; } = "";
    public string Date { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string? Link { get; set; }
}
