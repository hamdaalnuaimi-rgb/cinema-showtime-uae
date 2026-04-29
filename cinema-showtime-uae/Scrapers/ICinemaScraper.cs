using cinema_showtime_uae.Models;

namespace cinema_showtime_uae.Scrapers;

public interface ICinemaScraper
{
    string ChainName { get; }
    Task<CinemaResult> ScrapeAsync(int dateOffset = 0, CancellationToken ct = default);
}
