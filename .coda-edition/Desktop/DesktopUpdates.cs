using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace HowlingWhispers.CodaLauncher;

internal static class DesktopUpdates
{
    private static readonly HttpClient Http = CreateHttp();
    internal sealed record Update(string Version, string Url);

    public static async Task<Update?> FindAsync(string current, CancellationToken ct)
    {
        using var response = await Http.GetAsync("https://api.github.com/repos/HowlingWhispers/HW-CodaLauncher/releases?per_page=20", ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var suffix = (OperatingSystem.IsMacOS() ? "-macos-" : "-linux-")
            + (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64")
            + (OperatingSystem.IsMacOS() ? ".zip" : ".tar.gz");
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            var version = (release.GetProperty("tag_name").GetString() ?? "").TrimStart('v');
            if (!System.Version.TryParse(version.Split('-')[0], out var next)
                || !System.Version.TryParse(current.Split('-')[0], out var installed) || next <= installed) continue;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
                if ((asset.GetProperty("name").GetString() ?? "").EndsWith(suffix, StringComparison.Ordinal))
                    return new Update(version, asset.GetProperty("browser_download_url").GetString()!);
        }
        return null;
    }
    private static HttpClient CreateHttp() { var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) }; client.DefaultRequestHeaders.UserAgent.ParseAdd("CodaLauncher-Desktop/0.6"); return client; }
}
