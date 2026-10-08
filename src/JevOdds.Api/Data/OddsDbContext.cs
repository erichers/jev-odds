using Microsoft.EntityFrameworkCore;

namespace JevOdds.Api.Data;

public sealed class PriceBar
{
    public long Id { get; set; }

    public string Ticker { get; set; } = "";

    public DateOnly Date { get; set; }

    public double Close { get; set; }
}

public sealed class TickerCache
{
    public string Ticker { get; set; } = "";

    public string Provider { get; set; } = "";

    public DateTime FetchedAtUtc { get; set; }
}

public sealed class QueryRecord
{
    public long Id { get; set; }

    public string Ticker { get; set; } = "";

    public double Percent { get; set; }

    public string Direction { get; set; } = "";

    public DateOnly TargetDate { get; set; }

    public string VolWindow { get; set; } = "";

    public double? VolOverridePercent { get; set; }

    public string Drift { get; set; } = "";

    public double Spot { get; set; }

    public double Sigma { get; set; }

    public double AnalyticClose { get; set; }

    public double AnalyticTouch { get; set; }

    public double MonteCarloClose { get; set; }

    public double MonteCarloTouch { get; set; }

    public string Origin { get; set; } = "";

    public string Provider { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; }
}

public class OddsDbContext : DbContext
{
    public OddsDbContext(DbContextOptions<OddsDbContext> options) : base(options)
    {
    }

    protected OddsDbContext(DbContextOptions options) : base(options)
    {
    }

    public DbSet<PriceBar> PriceBars => Set<PriceBar>();

    public DbSet<TickerCache> TickerCaches => Set<TickerCache>();

    public DbSet<QueryRecord> QueryRecords => Set<QueryRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PriceBar>(entity =>
        {
            entity.HasKey(bar => bar.Id);
            entity.Property(bar => bar.Ticker).HasMaxLength(12);
            entity.HasIndex(bar => new { bar.Ticker, bar.Date }).IsUnique();
        });

        modelBuilder.Entity<TickerCache>(entity =>
        {
            entity.HasKey(cache => cache.Ticker);
            entity.Property(cache => cache.Ticker).HasMaxLength(12);
            entity.Property(cache => cache.Provider).HasMaxLength(16);
        });

        modelBuilder.Entity<QueryRecord>(entity =>
        {
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Ticker).HasMaxLength(12);
            entity.Property(record => record.Direction).HasMaxLength(8);
            entity.Property(record => record.VolWindow).HasMaxLength(16);
            entity.Property(record => record.Drift).HasMaxLength(16);
            entity.Property(record => record.Origin).HasMaxLength(16);
            entity.Property(record => record.Provider).HasMaxLength(16);
            entity.HasIndex(record => record.CreatedAtUtc);
        });
    }
}

public sealed class SqliteOddsDbContext : OddsDbContext
{
    public SqliteOddsDbContext(DbContextOptions<SqliteOddsDbContext> options) : base(options)
    {
    }
}

public sealed class MySqlOddsDbContext : OddsDbContext
{
    public MySqlOddsDbContext(DbContextOptions<MySqlOddsDbContext> options) : base(options)
    {
    }
}
