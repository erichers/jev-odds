using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JevOdds.Api.Data;

public sealed class SqliteOddsDbContextFactory : IDesignTimeDbContextFactory<SqliteOddsDbContext>
{
    public SqliteOddsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SqliteOddsDbContext>()
            .UseSqlite("Data Source=data/design-sqlite.db")
            .Options;
        return new SqliteOddsDbContext(options);
    }
}

public sealed class MySqlOddsDbContextFactory : IDesignTimeDbContextFactory<MySqlOddsDbContext>
{
    public MySqlOddsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MySqlOddsDbContext>()
            .UseMySql(
                "Server=127.0.0.1;Port=3306;Database=jev_odds;User=jev;Password=example;",
                new MySqlServerVersion(new Version(8, 0, 36)))
            .Options;
        return new MySqlOddsDbContext(options);
    }
}
