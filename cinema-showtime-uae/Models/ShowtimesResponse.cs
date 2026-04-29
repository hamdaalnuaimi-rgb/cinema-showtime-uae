namespace cinema_showtime_uae.Models;

public record ShowtimesResponse(
    string Date,
    DateTime GeneratedAt,
    List<CinemaResult> Cinemas,
    List<string> Errors
);
