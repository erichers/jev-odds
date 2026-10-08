using JevOdds.Api;
using JevOdds.Api.Data;
using JevOdds.Api.Pricing;
using JevOdds.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<OddsOptions>(builder.Configuration.GetSection(OddsOptions.Section));

AddOddsDatabase(builder);
builder.Services.AddHttpClient("market", client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("JevOdds/1.0 (educational)");
    client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
});
builder.Services.AddScoped<MarketDataService>();
builder.Services.AddScoped<OddsService>();
builder.Services.AddSingleton<PdfReportService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("dev", policy => policy
        .WithOrigins("http://localhost:4200", "http://127.0.0.1:4200")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();
var resolvedMySql = app.Configuration["Database:ResolvedServerVersion"];
if (!string.IsNullOrWhiteSpace(resolvedMySql))
{
    app.Logger.LogInformation("MySQL compatibility version {Version}", resolvedMySql);
}
var pathBase = app.Configuration["Odds:PathBase"];
if (!string.IsNullOrWhiteSpace(pathBase))
{
    var prefix = pathBase.Trim();
    if (!prefix.StartsWith('/'))
    {
        prefix = "/" + prefix;
    }

    prefix = prefix.TrimEnd('/');
    if (prefix.Length > 0)
    {
        app.UsePathBase(prefix);
    }
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OddsDbContext>();
    db.Database.Migrate();
    await MarketDataService.SeedAsync(db, app.Environment.ContentRootPath, app.Logger);
}

if (app.Environment.IsDevelopment())
{
    app.UseCors("dev");
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/history", async (int? limit, OddsDbContext db, CancellationToken cancellationToken) =>
{
    var take = Math.Clamp(limit ?? 12, 1, 50);
    var rows = await db.QueryRecords
        .AsNoTracking()
        .OrderByDescending(record => record.Id)
        .Take(take)
        .ToListAsync(cancellationToken);
    var items = rows.Select(record => new HistoryItem(
        record.Id,
        record.Ticker,
        TickerCatalog.NameOf(record.Ticker),
        record.Percent,
        record.Direction,
        record.TargetDate.ToString("yyyy-MM-dd"),
        record.VolWindow,
        record.VolOverridePercent,
        record.Drift,
        record.AnalyticClose,
        record.AnalyticTouch,
        record.Origin,
        record.CreatedAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")));
    return Results.Ok(items);
});

app.MapGet("/api/tickers", (string? q) => Results.Ok(TickerCatalog.Search(q)));

app.MapGet("/api/calendar", (DateOnly from, DateOnly to) =>
{
    var days = TradingCalendar.CountTradingDays(from, to);
    var effective = TradingCalendar.EffectiveSession(from, to);
    return Results.Ok(new CalendarResponse(
        from.ToString("yyyy-MM-dd"),
        to.ToString("yyyy-MM-dd"),
        days,
        effective?.ToString("yyyy-MM-dd")));
});

app.MapGet("/api/market/{ticker}", async (string ticker, OddsService odds, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await odds.SnapshotAsync(ticker, cancellationToken));
    }
    catch (OddsException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/odds", async (OddsRequest request, OddsService odds, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await odds.CalculateAsync(request, cancellationToken));
    }
    catch (OddsException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapGet("/api/odds/pdf", async (
    string ticker,
    double percent,
    string direction,
    DateOnly targetDate,
    string? volWindow,
    double? volOverridePercent,
    string? drift,
    OddsService odds,
    PdfReportService pdf,
    IOptions<OddsOptions> options,
    CancellationToken cancellationToken) =>
{
    try
    {
        var request = new OddsRequest
        {
            Ticker = ticker,
            Percent = percent,
            Direction = direction,
            TargetDate = targetDate,
            VolWindow = string.IsNullOrWhiteSpace(volWindow) ? "60" : volWindow,
            VolOverridePercent = volOverridePercent,
            Drift = string.IsNullOrWhiteSpace(drift) ? "zero" : drift
        };
        var result = await odds.CalculateAsync(request, cancellationToken);
        var link = PublicLinks.ResultPage(options.Value.PublicBaseUrl, request, result.Ticker);
        var bytes = pdf.Build(result, link);
        var fileName = $"jev-odds-{result.Ticker}-{result.TargetDate}.pdf";
        return Results.File(bytes, "application/pdf", fileName);
    }
    catch (OddsException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapFallbackToFile("index.html");
app.Run();

static void AddOddsDatabase(WebApplicationBuilder builder)
{
    var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
    var connection = builder.Configuration.GetConnectionString("Odds") ?? "Data Source=data/jev-odds.db";
    if (provider.Equals("MySql", StringComparison.OrdinalIgnoreCase))
    {
        connection = MySqlVersionResolver.WithUtf8Mb4(connection);
        var version = MySqlVersionResolver.Resolve(connection, builder.Configuration["Database:ServerVersion"]);
        builder.Configuration["Database:ResolvedServerVersion"] = version.ToString();
        builder.Services.AddDbContext<MySqlOddsDbContext>(options => options.UseMySql(connection, version));
        builder.Services.AddScoped<OddsDbContext>(services => services.GetRequiredService<MySqlOddsDbContext>());
        return;
    }

    if (!provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Database:Provider '{provider}' is not supported. Use Sqlite or MySql.");
    }

    var sqlite = PrepareSqlite(connection, builder.Environment.ContentRootPath);
    builder.Services.AddDbContext<SqliteOddsDbContext>(options => options.UseSqlite(sqlite));
    builder.Services.AddScoped<OddsDbContext>(services => services.GetRequiredService<SqliteOddsDbContext>());
}

static string PrepareSqlite(string connectionString, string contentRoot)
{
    var sqlite = new SqliteConnectionStringBuilder(connectionString);
    var path = sqlite.DataSource;
    if (!Path.IsPathRooted(path))
    {
        path = Path.GetFullPath(Path.Combine(contentRoot, path));
    }

    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    sqlite.DataSource = path;
    return sqlite.ToString();
}

public partial class Program;
