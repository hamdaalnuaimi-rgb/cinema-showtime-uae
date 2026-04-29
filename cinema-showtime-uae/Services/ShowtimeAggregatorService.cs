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

    public async Task<ShowtimesResponse> GetShowtimesAsync(int dateOffset = 0, CancellationToken ct = default)
    {
        dateOffset = Math.Clamp(dateOffset, 0, 2);

        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(4)).AddDays(dateOffset);
        var cacheKey = $"showtimes_{date:yyyy-MM-dd}";

        if (_cache.TryGetValue(cacheKey, out ShowtimesResponse? cached))
            return cached!;

        var tasks = _scrapers.Select(scraper => RunScraperAsync(scraper, dateOffset, ct)).ToArray();
        var results = await Task.WhenAll(tasks);

        var cinemas = results.Where(r => r.Result != null).Select(r => r.Result!).ToList();
        var errors  = results.Where(r => r.Error  != null).Select(r => r.Error!).ToList();

        var response = new ShowtimesResponse(
            Date: date.ToString("yyyy-MM-dd"),
            GeneratedAt: DateTime.UtcNow,
            Cinemas: cinemas,
            Errors: errors
        );

        _cache.Set(cacheKey, response, TimeSpan.FromHours(24));
        return response;
    }

    private static async Task<(CinemaResult? Result, string? Error)> RunScraperAsync(
        ICinemaScraper scraper, int dateOffset, CancellationToken ct)
    {
        try
        {
            var result = await scraper.ScrapeAsync(dateOffset, ct);
            return (result, null);
        }
        catch (Exception ex)
        {
            return (null, $"{scraper.ChainName}: {ex.Message}");
        }
    }
}
