using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using HowlingWhispers.CodaLauncher;

int assertions = 0;
void Check(bool ok, string description)
{
    assertions++;
    if (!ok) throw new Exception(description);
}
string root = Directory.GetCurrentDirectory();
var local = BundledNews.Load(root);
Check(local.Count >= 2, "Player-facing news must be included in repository release assets");
Check(local.Any(x => x.Id == "coda-companion-playtest"), "Coda Companion testing news included");
Check(local.All(x => !string.IsNullOrEmpty(x.Title) && !string.IsNullOrEmpty(x.Text)),
    "No empty news entries");

var cacheRoot = Path.Combine(Path.GetTempPath(), "howl-news-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(cacheRoot);
int packRequests = 0, newsRequests = 0;
string remoteJson = """
    [{"id":"latest-from-github","date":"2026-10-08","title":"Coda's new notice",
      "text":"News is pulled from GitHub, no deployment required."}]
    """;
var handler = new FakeHandler(req =>
{
    if (req.RequestUri?.ToString() == GitHubNewsService.Url)
    {
        newsRequests++;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(remoteJson, Encoding.UTF8, "application/json")
        };
    }
    packRequests++;
    var remote = new LauncherFeed
    {
        Project = "Howling Whispers",
        Packs = [new PackCatalogInfo { Id = "remote-pack", Name = "Remote pack" }],
        News = [new NewsItem { Id = "server-only", Title = "Server news must not appear", Text = "Remote" }]
    };
    return new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(remote), Encoding.UTF8, "application/json")
    };
});
using var client = new HttpClient(handler);
var service = new FeedService(client, root);
var one = await service.FetchAsync("https://example.com/launcher", CancellationToken.None);
Check(one.Online, "Pack catalog remains available online");
Check(one.Packs.Count == 1 && one.Packs[0].Id == "remote-pack",
    "Server metadata remains accessible for the pack installer");
Check(one.NewsSource == "GitHub" && one.News.Any(x => x.Id == "latest-from-github"),
    "GitHub raw file overrides bundled news while reachable");
Check(one.News.All(x => x.Id != "server-only"),
    "Server news is not displayed in launcher");
one.Packs[0].Name = "modified by caller";
var two = await service.FetchAsync("https://example.com/launcher", CancellationToken.None);
Check(packRequests == 1 && newsRequests == 1,
    "UI state refresh uses one cached server metadata and GitHub news request");
Check(two.Packs[0].Name == "Remote pack", "Feed cache is not caller-mutable");

var offlineCalls = 0;
using var down = new HttpClient(new FakeHandler(_ =>
{
    offlineCalls++;
    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
}));
var fallbackRoot = Path.Combine(cacheRoot, "offline");
Directory.CreateDirectory(fallbackRoot);
File.Copy(Path.Combine(root, "web", "news.json"), Path.Combine(fallbackRoot, "news.json"));
var unavailable = new FeedService(down, fallbackRoot);
var fallback = await unavailable.FetchAsync("https://example.com/launcher", CancellationToken.None);
Check(!fallback.Online && fallback.Error != null, "HTTP error is preserved for pack metadata");
Check(fallback.NewsSource == "Bundled" && fallback.News.Any(x => x.Id == "coda-companion-playtest"),
    "News displays offline from bundled copy");
await unavailable.FetchAsync("https://example.com/launcher", CancellationToken.None);
Check(offlineCalls == 2, "Repeated outages do not spam news or metadata services");
Check(BundledNews.Load(Path.Combine(root, "missing-fixture")).Count >= 1,
    "Broken/missing news asset cannot block launch");

int remoteRequests = 0;
using var dedicatedHttp = new HttpClient(new FakeHandler(_ =>
{
    remoteRequests++;
    return new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(remoteJson, Encoding.UTF8, "application/json")
    };
}));
var remoteNews = new GitHubNewsService(dedicatedHttp, root, cacheRoot);
var remoteResult = await remoteNews.FetchAsync(CancellationToken.None);
Check(remoteResult.Source == "GitHub" && remoteRequests == 1,
    "GitHub news fetched directly without API.github.com");
var offlineNews = new GitHubNewsService(down, root, cacheRoot);
var cached = await offlineNews.FetchAsync(CancellationToken.None);
Check(cached.Source == "Cached" && cached.Items[0].Id == "latest-from-github",
    "Previously downloaded GitHub news survives offline launcher restart");
Console.WriteLine("PASS: " + assertions + " bundled news, online pack metadata, caching and offline assertions");

sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(respond(request));
}
