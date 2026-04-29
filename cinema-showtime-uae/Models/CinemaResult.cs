namespace cinema_showtime_uae.Models;

public record CinemaResult(
    string Chain,
    List<MovieResult> Movies
);
