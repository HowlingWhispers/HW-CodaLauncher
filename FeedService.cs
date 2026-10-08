using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace HowlingWhispers.CodaLauncher;

/// <summary>
/// Server calls are reserved for pack and resource metadata, not launcher news.
/// Coalesce repeated UI refreshes so every view switch or settings save does not
/// generate a new request. Bundled news works fully offline and is never
/// replaced by server-generated announcements or HTTP failures.
/// </summary>
internal sealed class FeedService
{
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(4) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly TimeSpan SuccessTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan OfflineTtl = TimeSpan.FromMinutes(2);

    private readonly HttpClient _http;
    private readonly GitHubNewsService _news;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LauncherFeed? _cache;
    private string? _cachedUrl;
    private DateTimeOffset _expires;

    internal FeedService(HttpClient? http = null, string? newsRoot = null)
    {
        _http = http ?? SharedHttp;
        _news = new GitHubNewsService(_http, newsRoot, newsRoot);
    }

    internal async Task<LauncherFeed> FetchAsync(string raw, CancellationToken ct)
    {
        var (news, newsSource) = await _news.FetchAsync(ct);
        if (string.IsNullOrWhiteSpace(raw))
            return Offline("Launcher pack catalog URL is not configured.", news, newsSource);

        var url = raw.TrimEnd('/');
        await _gate.WaitAsync(ct);
        try
        {
            if (_cache is not null && _cachedUrl == url && DateTimeOffset.UtcNow < _expires)
                return CopyWithNews(_cache, news, newsSource);

            try
            {
                var endpoint = new Uri(new Uri(url + "/"), "api/feed");
                using var response = await _http.GetAsync(endpoint, ct);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                var feed = await JsonSerializer.DeserializeAsync<LauncherFeed>(stream, Json, ct)
                    ?? throw new InvalidDataException("Empty launcher pack catalog.");
                feed.Online = true;
                feed.Error = null;
                feed.News = news;
                feed.NewsSource = newsSource;
                _cache = feed;
                _cachedUrl = url;
                _expires = DateTimeOffset.UtcNow.Add(SuccessTtl);
                return CopyWithNews(feed, news, newsSource);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested
                && ex is HttpRequestException or IOException or JsonException
                    or TaskCanceledException or UriFormatException)
            {
                var offline = _cache is not null && _cachedUrl == url
                    ? CopyWithNews(_cache, news, newsSource)
                    : Offline(ex.Message, news, newsSource);
                offline.Online = false;
                offline.Error = ex.Message;
                _cache = offline;
                _cachedUrl = url;
                _expires = DateTimeOffset.UtcNow.Add(OfflineTtl);
                return CopyWithNews(offline, news, newsSource);
            }
        }
        finally { _gate.Release(); }
    }

    private static LauncherFeed CopyWithNews(LauncherFeed source, List<NewsItem> news, string sourceName)
    {
        // Pack catalog compatibility functions may mutate feed data. Do not
        // let callers accidentally mutate the in-memory HTTP response cache.
        var copy = JsonSerializer.Deserialize<LauncherFeed>(JsonSerializer.Serialize(source, Json), Json)
            ?? new LauncherFeed();
        copy.News = news;
        copy.NewsSource = sourceName;
        return copy;
    }

    private static LauncherFeed Offline(string reason, List<NewsItem> news, string sourceName) => new()
    {
        Online = false,
        Error = reason,
        News = news,
        NewsSource = sourceName
    };
}
