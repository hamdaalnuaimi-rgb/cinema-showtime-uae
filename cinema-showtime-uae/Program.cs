using cinema_showtime_uae.Scrapers;
using cinema_showtime_uae.Services;

var builder = WebApplication.CreateBuilder(args);

// Railway (and most cloud hosts) inject PORT — bind to it
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("CinemaUae", c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.Add("User-Agent",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
});

// Register one scraper per UAE cinema chain
// Args: (displayName, cinemauae.com slug, chain keyword in anchor text)
// Trailing spaces omitted so suffix-style links ("X Cinema - Chain") are matched too
(string Name, string Slug, string Prefix)[] chains =
[
    ("VOX Cinemas",    "vox",          "Vox"),
    ("Reel Cinemas",   "reel",         "Reel"),
    ("Novo Cinemas",   "novo",         "Novo"),
    ("Roxy Cinemas",   "roxy-cinemas", "Roxy"),
    ("Star Cinemas",   "star",         "Star"),
    ("Cinemacity",     "cinemacity",   "Cinemacity"),
    ("Cinemax",        "cinemax",      "Cinemax"),
    ("Cinepolis",      "cinepolis",    "Cinepolis"),
    ("Cine Royal",     "cineroyal",    "Cine Royal"),
];

foreach (var (name, slug, prefix) in chains)
    builder.Services.AddScoped<ICinemaScraper>(sp =>
        new CinemaUaeScraper(sp.GetRequiredService<IHttpClientFactory>(), name, slug, prefix));

builder.Services.AddScoped<ShowtimeAggregatorService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Cinema Showtime UAE v1"));

app.UseDefaultFiles();   // serves index.html for "/"
app.UseStaticFiles();    // serves wwwroot/

app.MapControllers();

// Pre-warm the cache in the background so the first browser request is fast
_ = Task.Run(async () =>
{
    await Task.Delay(TimeSpan.FromSeconds(3));
    using var scope = app.Services.CreateScope();
    var svc = scope.ServiceProvider.GetRequiredService<ShowtimeAggregatorService>();
    await svc.GetShowtimesAsync(0);
});

app.Run();
