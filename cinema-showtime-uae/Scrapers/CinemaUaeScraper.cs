using System.Text.RegularExpressions;
using cinema_showtime_uae.Models;

namespace cinema_showtime_uae.Scrapers;

public class CinemaUaeScraper : ICinemaScraper
{
    private const string BaseUrl = "https://cinemauae.com";

    private readonly HttpClient _http;
    private readonly string _slug;
    private readonly string _textPrefix;

    public string ChainName { get; }

    // slug = cinemauae.com path segment, textPrefix = word used in link text on that page
    public CinemaUaeScraper(IHttpClientFactory factory, string chainName, string slug, string textPrefix)
    {
        ChainName = chainName;
        _slug = slug;
        _textPrefix = textPrefix;
        _http = factory.CreateClient("CinemaUae");
    }

    public async Task<CinemaResult> ScrapeAsync(int dateOffset = 0, CancellationToken ct = default)
    {
        var chainHtml = await _http.GetStringAsync($"{BaseUrl}/{_slug}/", ct);
        var locationUrls = ExtractLocationUrls(chainHtml);

        // Fetch all location pages in parallel (capped at 8 concurrent)
        var sem = new SemaphoreSlim(8);
        var locationTasks = locationUrls.Select(async loc =>
        {
            await sem.WaitAsync(ct);
            try
            {
                var html = await _http.GetStringAsync(loc.Url, ct);
                var tabHtml = ExtractTabContent(html, dateOffset);
                return (loc.Name, loc.City, Movies: ExtractMovies(tabHtml));
            }
            catch { return (loc.Name, loc.City, Movies: new List<(string Title, List<string> Times)>()); }
            finally { sem.Release(); }
        });

        var locationResults = await Task.WhenAll(locationTasks);

        // Group: movie → (locationName, city) → merged+deduplicated showtimes
        var movieMap = new Dictionary<string, Dictionary<(string Name, string City), SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (locationName, city, movies) in locationResults)
        {
            foreach (var (title, times) in movies)
            {
                if (!movieMap.TryGetValue(title, out var locMap))
                    movieMap[title] = locMap = new();
                var key = (locationName, city);
                if (!locMap.TryGetValue(key, out var timeSet))
                    locMap[key] = timeSet = new(StringComparer.OrdinalIgnoreCase);
                foreach (var t in times) timeSet.Add(t);
            }
        }

        var movieResults = movieMap
            .Select(kv => new MovieResult(
                kv.Key,
                kv.Value.Select(loc => new LocationResult(loc.Key.Name, loc.Key.City, [.. loc.Value])).ToList()))
            .OrderBy(m => m.Title)
            .ToList();

        return new CinemaResult(ChainName, movieResults);
    }

    // Each cinema page embeds 3 day-tabs: showtimestab1=today, tab2=tomorrow, tab3=day after
    private static string ExtractTabContent(string html, int dateOffset)
    {
        var tabId   = $"showtimestab{dateOffset + 1}";
        var nextId  = $"showtimestab{dateOffset + 2}";

        var start = html.IndexOf($"id=\"{tabId}\"", StringComparison.Ordinal);
        if (start < 0) return html;

        var end = html.IndexOf($"id=\"{nextId}\"", start, StringComparison.Ordinal);
        return end < 0 ? html[start..] : html[start..end];
    }

    private List<(string Name, string City, string Url)> ExtractLocationUrls(string html)
    {
        var results = new List<(string, string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var pattern = new Regex(
            @"<a\s+href=""(/[^""]+)""[^>]*>([^<]*" + Regex.Escape(_textPrefix) + @"[^<]*)</a>",
            RegexOptions.IgnoreCase);

        foreach (Match m in pattern.Matches(html))
        {
            var href = m.Groups[1].Value.Trim();
            var text = m.Groups[2].Value.Trim();

            if (!seen.Add(href)) continue;

            // Strip chain prefix ("Vox X Cinema") or chain suffix ("X Cinema - Vox")
            var name = text;
            if (name.StartsWith(_textPrefix, StringComparison.OrdinalIgnoreCase))
                name = name[_textPrefix.Length..].Trim();
            var dashSuffix = " - " + _textPrefix.TrimEnd();
            if (name.EndsWith(dashSuffix, StringComparison.OrdinalIgnoreCase))
                name = name[..^dashSuffix.Length].Trim();
            if (name.EndsWith(" Cinema", StringComparison.OrdinalIgnoreCase))
                name = name[..^7].Trim();

            // Extract city from URL segment e.g. "/dubai/mall-cinema/" → "Dubai"
            var city = CityFromHref(href);

            results.Add((name, city, BaseUrl + href));
        }

        return results;
    }

    private static string CityFromHref(string href)
    {
        var segment = href.TrimStart('/').Split('/')[0];
        return segment switch
        {
            "abu-dhabi"     => "Abu Dhabi",
            "ras-al-khaimah"=> "Ras Al Khaimah",
            "umm-al-quwain" => "Umm Al Quwain",
            "al-ain"        => "Al Ain",
            _ when segment.Length > 0 => char.ToUpper(segment[0]) + segment[1..]
        };
    }

    private static readonly Regex _movieHeading = new(
        @"<h4>\s*(.+?)\s*Showtimes:\s*</h4>(.*?)(?=<h4>|<hr\b|</section|</article|$)",
        RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex _timePattern = new(
        @">(\d{1,2}:\d{2}\s*[AP]M)\s*<",
        RegexOptions.IgnoreCase);

    private static List<(string Title, List<string> Times)> ExtractMovies(string html)
    {
        var results = new List<(string, List<string>)>();

        foreach (Match m in _movieHeading.Matches(html))
        {
            var title = m.Groups[1].Value.Trim();
            var block = m.Groups[2].Value;

            var times = _timePattern.Matches(block)
                .Select(t => t.Groups[1].Value.Trim())
                .ToList();

            if (times.Count > 0)
                results.Add((title, times));
        }

        return results;
    }
}
