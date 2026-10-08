using System.IO;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace HowlingWhispers.CodaLauncher;

/// <summary>
/// Public GitHub Releases Atom feed backup for anonymous GitHub REST API rate
/// limits. Only accepts links to THIS project's published release tags; actual
/// binaries are accepted only after separate SHA-256 verification by installers.
/// </summary>
internal static class NightlyAtomReleaseReader
{
    internal const string FeedUrl = "https://github.com/HowlingWhispers/HW-Mods/releases.atom";
    private const string TagPath = "/HowlingWhispers/HW-Mods/releases/tag/";
    private const string DownloadPath = "/HowlingWhispers/HW-Mods/releases/download/";
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    internal static NightlyReleaseSelector.Release? SelectNewest(string xml, string prefix,
        string packageName, string checksumName)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 512 * 1024
        });
        var document = XDocument.Load(reader);
        if (document.Root?.Name != Atom + "feed")
            throw new InvalidDataException("GitHub release feed is not Atom XML.");
        NightlyReleaseSelector.Release? newest = null;
        foreach (var entry in document.Root.Elements(Atom + "entry"))
        {
            string time = (string?) entry.Element(Atom + "published")
                ?? (string?) entry.Element(Atom + "updated") ?? "";
            if (!DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var published)) continue;

            var link = entry.Elements(Atom + "link")
                .FirstOrDefault(element => (string?)element.Attribute("rel") == "alternate"
                    && (string?)element.Attribute("href") != null);
            string? raw = (string?)link?.Attribute("href");
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var url)
                || url.Scheme != Uri.UriSchemeHttps
                || !url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                || !url.AbsolutePath.StartsWith(TagPath, StringComparison.Ordinal))
                continue;

            string tag = Uri.UnescapeDataString(url.AbsolutePath[TagPath.Length..]);
            // Tag must be one safe path segment, never redirect to arbitrary URLs.
            if (!tag.StartsWith(prefix, StringComparison.Ordinal)
                || tag.Length > 130 || tag.Length <= prefix.Length
                || !tag.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_' || c == '.'))
                continue;
            string downloadBase = "https://github.com" + DownloadPath + tag + "/";
            var candidate = new NightlyReleaseSelector.Release(tag,
                new Uri(downloadBase + Uri.EscapeDataString(packageName)),
                new Uri(downloadBase + Uri.EscapeDataString(checksumName)), published);
            if (newest is null || candidate.Published > newest.Published
                || (candidate.Published == newest.Published
                    && string.CompareOrdinal(candidate.Tag, newest.Tag) > 0))
                newest = candidate;
        }
        return newest;
    }
}
