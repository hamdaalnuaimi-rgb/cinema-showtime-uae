using Microsoft.Extensions.Caching.Memory;
using cinema_showtime_uae.Models;
using cinema_showtime_uae.Scrapers;

namespace cinema_showtime_uae.Services;

public class ShowtimeAggregatorService
{
    private readonly IEnumerable<ICinemaScraper> _scrapers;
    private readonly IMemoryCache _cache;
    private readonly ImdbRatingFetcher _imdbFetcher;

    public ShowtimeAggregatorService(IEnumerable<ICinemaScraper> scrapers, IMemoryCache cache, ImdbRatingFetcher imdbFetcher)
    {
        _scrapers = scrapers;
        _cache = cache;
        _imdbFetcher = imdbFetcher;
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

        await EnrichMovieRatingsAsync(cinemas, ct);

        var response = new ShowtimesResponse(
            Date: date.ToString("yyyy-MM-dd"),
            GeneratedAt: DateTime.UtcNow,
            Cinemas: cinemas,
            Errors: errors
        );

        _cache.Set(cacheKey, response, TimeSpan.FromHours(24));
        return response;
    }

    private async Task EnrichMovieRatingsAsync(List<CinemaResult> cinemas, CancellationToken ct)
    {
        var uniqueMovies = cinemas
            .SelectMany(c => c.Movies)
            .Select(m => m.Title)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ratingTasks = uniqueMovies.Select(title => _imdbFetcher.GetRatingAsync(title, ct)).ToList();
        var ratings = await Task.WhenAll(ratingTasks);

        var ratingMap = uniqueMovies.Zip(ratings).ToDictionary(x => x.First, x => x.Second, StringComparer.OrdinalIgnoreCase);

        foreach (var cinema in cinemas)
        {
            cinema.Movies.ForEach(movie =>
            {
                if (ratingMap.TryGetValue(movie.Title, out var rating))
                {
                    var updatedMovie = movie with { Rating = rating };
                    var index = cinema.Movies.IndexOf(movie);
                    cinema.Movies[index] = updatedMovie;
                }
            });
        }
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
