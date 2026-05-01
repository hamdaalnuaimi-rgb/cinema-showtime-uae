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
            catch { return (loc.Name, loc.City, Movies: new List<(string Title, List<ShowtimeEntry> Showtimes)>()); }
            finally { sem.Release(); }
        });

        var locationResults = await Task.WhenAll(locationTasks);

        // Group: movie → (locationName, city) → merged+deduplicated showtimes
        var movieMap = new Dictionary<string, Dictionary<(string Name, string City), HashSet<ShowtimeEntry>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (locationName, city, movies) in locationResults)
        {
            foreach (var (title, showtimes) in movies)
            {
                if (!movieMap.TryGetValue(title, out var locMap))
                    movieMap[title] = locMap = new();
                var key = (locationName, city);
                if (!locMap.TryGetValue(key, out var timeSet))
                    locMap[key] = timeSet = new();
                foreach (var s in showtimes) timeSet.Add(s);
            }
        }

        var movieResults = movieMap
            .Select(kv => new MovieResult(
                kv.Key,
                kv.Value.Select(loc => new LocationResult(
                    loc.Key.Name,
                    loc.Key.City,
                    loc.Value.OrderBy(s => ParseMinutes(s.Time)).ToList()
                )).ToList()))
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

            // Strip chain prefix ("Vox X Cinema") or chain suffix ("X Cinema – Roxy" / "X Cinema - Chain")
            var name = text;
            if (name.StartsWith(_textPrefix, StringComparison.OrdinalIgnoreCase))
                name = name[_textPrefix.Length..].Trim();
            var chainName = _textPrefix.TrimEnd();
            foreach (var sep in new[] { " – ", " - " })
            {
                var suffix = sep + chainName;
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name[..^suffix.Length].Trim();
                    break;
                }
            }
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

    // [^<]+ stops the title capture at the first HTML tag, preventing runaway matches
    private static readonly Regex _movieHeading = new(
        @"<h4>\s*([^<]+?)\s*Showtimes:\s*</h4>(.*?)(?=<h4>|<hr\b|</section|</article|$)",
        RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex _buttonBlock = new(
        @"<button\b[^>]*\bbtn-timings\b[^>]*>(.*?)</button>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex _timeInButton = new(
        @"(\d{1,2}:\d{2}\s*[AP]M)",
        RegexOptions.IgnoreCase);

    private static readonly Regex _typeInButton = new(
        @"<small>\s*([^<]+?)\s*</small>",
        RegexOptions.IgnoreCase);

    private static List<(string Title, List<ShowtimeEntry> Showtimes)> ExtractMovies(string html)
    {
        var results = new List<(string, List<ShowtimeEntry>)>();

        foreach (Match m in _movieHeading.Matches(html))
        {
            var title = m.Groups[1].Value.Trim();
            var block = m.Groups[2].Value;

            var showtimes = new List<ShowtimeEntry>();
            foreach (Match btn in _buttonBlock.Matches(block))
            {
                var btnHtml = btn.Groups[1].Value;
                var timeMatch = _timeInButton.Match(btnHtml);
                if (!timeMatch.Success) continue;
                var time = timeMatch.Groups[1].Value.Trim();
                var typeMatch = _typeInButton.Match(btnHtml);
                var type = typeMatch.Success ? typeMatch.Groups[1].Value.Trim() : "";
                showtimes.Add(new ShowtimeEntry(time, type));
            }

            if (showtimes.Count > 0)
                results.Add((title, showtimes));
        }

        return results;
    }

    private static readonly Regex _parseTime = new(@"(\d+):(\d+)\s*(AM|PM)", RegexOptions.IgnoreCase);
    private static int ParseMinutes(string t)
    {
        var m = _parseTime.Match(t);
        if (!m.Success) return 0;
        var h = int.Parse(m.Groups[1].Value);
        var min = int.Parse(m.Groups[2].Value);
        var pm = m.Groups[3].Value.Equals("PM", StringComparison.OrdinalIgnoreCase);
        if (pm && h != 12) h += 12;
        if (!pm && h == 12) h = 0;
        return h * 60 + min;
    }
}
