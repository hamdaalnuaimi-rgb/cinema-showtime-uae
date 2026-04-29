using Microsoft.AspNetCore.Mvc;
using cinema_showtime_uae.Services;

namespace cinema_showtime_uae.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ShowtimesController : ControllerBase
{
    private readonly ShowtimeAggregatorService _aggregator;

    public ShowtimesController(ShowtimeAggregatorService aggregator)
    {
        _aggregator = aggregator;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int dateOffset = 0, CancellationToken ct = default)
    {
        var result = await _aggregator.GetShowtimesAsync(dateOffset, ct);
        return Ok(result);
    }
}
