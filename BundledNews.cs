using System.IO;
using System.Text.Json;

namespace HowlingWhispers.CodaLauncher;

/// <summary>
/// Player-facing launcher news is part of the application, not an HTTP feed.
/// Updating the launcher updates the bulletin board on every platform.
/// </summary>
internal static class BundledNews
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    internal static List<NewsItem> Load(string? installationRoot = null)
    {
        var root = installationRoot ?? AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(root, "web", "news.json"),
            Path.Combine(root, "news.json")
        };
        foreach (var filename in candidates)
        {
            if (!File.Exists(filename)) continue;
            try
            {
                var entries = JsonSerializer.Deserialize<List<NewsItem>>(File.ReadAllText(filename), Json);
                if (entries is not null && entries.Count > 0)
                    return entries.Where(x => !string.IsNullOrWhiteSpace(x.Title)
                        && !string.IsNullOrWhiteSpace(x.Text))
                        .Take(12).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Never make the news panel a reason the game cannot start.
            }
        }
        return [new NewsItem
        {
            Id = "bundled-news-unavailable",
            Date = "",
            Title = "Coda's noticeboard",
            Text = "The next CodaLauncher update will bring more news from the workshop."
        }];
    }
}
