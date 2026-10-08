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

int requests = 0;
var handler = new FakeHandler(_ =>
{
    requests++;
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
Check(one.News.Any(x => x.Id == "coda-companion-playtest"),
    "Bundled news displayed while server is online");
Check(one.News.All(x => x.Id != "server-only"),
    "Server news is not fetched into launcher presentation");
one.Packs[0].Name = "modified by caller";
var two = await service.FetchAsync("https://example.com/launcher", CancellationToken.None);
Check(requests == 1, "UI state refresh uses one cached server metadata request");
Check(two.Packs[0].Name == "Remote pack", "Feed cache is not caller-mutable");

int failures = 0;
using var down = new HttpClient(new FakeHandler(_ =>
{
    failures++;
    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
}));
var unavailable = new FeedService(down, root);
var fallback = await unavailable.FetchAsync("https://example.com/launcher", CancellationToken.None);
Check(!fallback.Online && fallback.Error != null, "HTTP error is preserved for pack metadata");
Check(fallback.News.Any(x => x.Id == "coda-companion-playtest"),
    "News displays offline even if server is unavailable");
await unavailable.FetchAsync("https://example.com/launcher", CancellationToken.None);
Check(failures == 1, "Repeated outage does not spam the server");
Check(BundledNews.Load(Path.Combine(root, "missing-fixture")).Count >= 1,
    "Broken/missing news asset cannot block launch");
Console.WriteLine("PASS: " + assertions + " bundled news, online pack metadata, caching and offline assertions");

sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(respond(request));
}
