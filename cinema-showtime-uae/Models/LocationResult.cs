namespace cinema_showtime_uae.Models;

public record LocationResult(
    string Name,
    string City,
    List<ShowtimeEntry> Showtimes
);
