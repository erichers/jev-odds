using JevOdds.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JevOdds.Tests;

public class HistoryStoreTests
{
    [Fact]
    public async Task Sqlite_migration_stores_a_query()
    {
        var path = Path.Combine(Path.GetTempPath(), $"jev-odds-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<SqliteOddsDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;

        try
        {
            await using var db = new SqliteOddsDbContext(options);
            await db.Database.MigrateAsync();
            db.QueryRecords.Add(new QueryRecord
            {
                Ticker = "SPY",
                Percent = 5,
                Direction = "up",
                TargetDate = new DateOnly(2027, 1, 15),
                VolWindow = "60",
                Drift = "zero",
                Spot = 100,
                Sigma = 0.2,
                AnalyticClose = 0.2,
                AnalyticTouch = 0.4,
                MonteCarloClose = 0.19,
                MonteCarloTouch = 0.39,
                Origin = "cached",
                Provider = "seed",
                CreatedAtUtc = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc)
            });
            await db.SaveChangesAsync();

            var saved = await db.QueryRecords.SingleAsync();
            Assert.Equal("SPY", saved.Ticker);
            Assert.Equal(0.2, saved.AnalyticClose);
            Assert.True(await db.PriceBars.AnyAsync() == false);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
