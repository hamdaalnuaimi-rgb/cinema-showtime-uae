using Microsoft.Extensions.Caching.Memory;
using cinema_showtime_uae.Models;
using cinema_showtime_uae.Scrapers;

namespace cinema_showtime_uae.Services;

public class ShowtimeAggregatorService
{
    private readonly IEnumerable<ICinemaScraper> _scrapers;
    private readonly IMemoryCache _cache;

    public ShowtimeAggregatorService(IEnumerable<ICinemaScraper> scrapers, IMemoryCache cache)
    {
        _scrapers = scrapers;
        _cache = cache;
    }

    public async Task<ShowtimesResponse> GetTodayShowtimesAsync(CancellationToken ct = default)
    {
        // UAE is UTC+4
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(4));
        var cacheKey = $"showtimes_{today:yyyy-MM-dd}";

        if (_cache.TryGetValue(cacheKey, out ShowtimesResponse? cached))
            return cached!;

        var tasks = _scrapers.Select(scraper => RunScraperAsync(scraper, ct)).ToArray();
        var results = await Task.WhenAll(tasks);

        var cinemas = results.Where(r => r.Result != null).Select(r => r.Result!).ToList();
        var errors = results.Where(r => r.Error != null).Select(r => r.Error!).ToList();

        var response = new ShowtimesResponse(
            Date: today.ToString("yyyy-MM-dd"),
            GeneratedAt: DateTime.UtcNow,
            Cinemas: cinemas,
            Errors: errors
        );

        _cache.Set(cacheKey, response, TimeSpan.FromHours(24));

        return response;
    }

    private static async Task<(CinemaResult? Result, string? Error)> RunScraperAsync(
        ICinemaScraper scraper, CancellationToken ct)
    {
        try
        {
            var result = await scraper.ScrapeAsync(ct);
            return (result, null);
        }
        catch (Exception ex)
        {
            return (null, $"{scraper.ChainName}: {ex.Message}");
        }
    }
}
