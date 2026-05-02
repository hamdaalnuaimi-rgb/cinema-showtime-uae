using System.Text.RegularExpressions;

namespace cinema_showtime_uae.Services;

public class ImdbRatingFetcher
{
    private readonly HttpClient _http;
    private readonly ILogger<ImdbRatingFetcher> _logger;

    public ImdbRatingFetcher(HttpClient http, ILogger<ImdbRatingFetcher> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<string?> GetRatingAsync(string movieTitle, CancellationToken ct = default)
    {
        try
        {
            var searchUrl = $"https://www.imdb.com/find?q={Uri.EscapeDataString(movieTitle)}&s=tt";
            var html = await _http.GetStringAsync(searchUrl, ct);

            var match = Regex.Match(html, @"<a\s+href=""(/title/(tt\d+)/?)""[^>]*>([^<]*)" + Regex.Escape(movieTitle) + @"([^<]*)</a>", RegexOptions.IgnoreCase);
            if (!match.Success) return null;

            var titleId = match.Groups[2].Value;
            var titleUrl = $"https://www.imdb.com/title/{titleId}/";
            var titleHtml = await _http.GetStringAsync(titleUrl, ct);

            var ratingMatch = Regex.Match(titleHtml, @"""ratingValue""\s*:\s*""([\d.]+)""");
            if (ratingMatch.Success)
            {
                return ratingMatch.Groups[1].Value;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to fetch IMDb rating for {movieTitle}: {ex.Message}");
            return null;
        }
    }
}
