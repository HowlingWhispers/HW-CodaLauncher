using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace HowlingWhispers.CodaLauncher;

/// <summary>
/// Read-only public GitHub Raw news. No authenticated GitHub REST calls and
/// no deployment requirement for editing announcements.
/// A local bundled JSON and a validated disk snapshot provide offline news.
/// </summary>
internal sealed class GitHubNewsService
{
    internal const string Url =
        "https://raw.githubusercontent.com/HowlingWhispers/HW-CodaLauncher/main/web/news.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(60);
    private const int MaxBytes = 64 * 1024;
    private readonly HttpClient _http;
    private readonly string? _bundledRoot;
    private readonly string _cacheFile;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _nextCheck;
    private string? _etag;
    private List<NewsItem>? _cached;

    internal GitHubNewsService(HttpClient http, string? bundledRoot = null, string? cacheRoot = null)
    {
        _http = http;
        _bundledRoot = bundledRoot;
        var folder = cacheRoot ?? Path.Combine(AppPaths.InstallRoot, "launcher");
        _cacheFile = Path.Combine(folder, "news-cache.json");
        _cached = LoadSaved(_cacheFile);
    }

    internal async Task<(List<NewsItem> Items, string Source)> FetchAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_nextCheck <= DateTimeOffset.UtcNow)
            {
                _nextCheck = DateTimeOffset.UtcNow.Add(RefreshInterval);
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, Url);
                    request.Headers.UserAgent.ParseAdd("CodaLauncher-News/1.0");
                    if (!string.IsNullOrWhiteSpace(_etag)
                        && EntityTagHeaderValue.TryParse(_etag, out var tag))
                        request.Headers.IfNoneMatch.Add(tag);
                    using var response = await _http.SendAsync(request,
                        HttpCompletionOption.ResponseHeadersRead, ct);
                    if (response.StatusCode == HttpStatusCode.NotModified && _cached is not null)
                        return (Copy(_cached), "GitHub");
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength is > MaxBytes)
                        throw new InvalidDataException("News file exceeds size limit.");
                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var buffer = new MemoryStream();
                    var chunk = new byte[8192];
                    int count;
                    while ((count = await stream.ReadAsync(chunk.AsMemory(), ct)) > 0)
                    {
                        if (buffer.Length + count > MaxBytes)
                            throw new InvalidDataException("News file exceeds size limit.");
                        buffer.Write(chunk, 0, count);
                    }
                    var items = Validate(JsonSerializer.Deserialize<List<NewsItem>>(buffer.ToArray(), Json));
                    if (items.Count == 0)
                        throw new InvalidDataException("News file has no valid entries.");
                    _cached = items;
                    _etag = response.Headers.ETag?.ToString();
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);
                        string temp = _cacheFile + ".tmp";
                        File.WriteAllText(temp, JsonSerializer.Serialize(items, Json));
                        File.Move(temp, _cacheFile, true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Read-only systems still display the fetched news.
                    }
                    return (Copy(items), "GitHub");
                }
                catch (Exception ex) when (!ct.IsCancellationRequested
                    && ex is HttpRequestException or IOException
                        or JsonException or TaskCanceledException)
                {
                    // Back off until the next scheduled refresh. Never block PLAY.
                }
            }
            if (_cached is { Count: > 0 }) return (Copy(_cached), "Cached");
            return (BundledNews.Load(_bundledRoot), "Bundled");
        }
        finally { _gate.Release(); }
    }

    private static List<NewsItem>? LoadSaved(string file)
    {
        try
        {
            if (!File.Exists(file) || new FileInfo(file).Length > MaxBytes) return null;
            var items = Validate(JsonSerializer.Deserialize<List<NewsItem>>(File.ReadAllText(file), Json));
            return items.Count == 0 ? null : items;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static List<NewsItem> Validate(List<NewsItem>? entries) => (entries ?? [])
        .Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Id)
            && !string.IsNullOrWhiteSpace(x.Title)
            && !string.IsNullOrWhiteSpace(x.Text)
            && x.Id.Length <= 80 && x.Title.Length <= 140 && x.Text.Length <= 1000
            && (string.IsNullOrEmpty(x.Link)
                || (Uri.TryCreate(x.Link, UriKind.Absolute, out var uri)
                    && uri.Scheme == Uri.UriSchemeHttps)))
        .Take(12)
        .ToList();

    private static List<NewsItem> Copy(List<NewsItem> items) =>
        items.Select(item => new NewsItem
        {
            Id = item.Id, Date = item.Date, Title = item.Title,
            Text = item.Text, Link = item.Link
        }).ToList();
}
