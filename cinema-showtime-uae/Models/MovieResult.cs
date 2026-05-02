namespace cinema_showtime_uae.Models;

public record MovieResult(
    string Title,
    List<LocationResult> Locations,
    string? Rating = null
);
