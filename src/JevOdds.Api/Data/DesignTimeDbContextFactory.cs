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
        var connection = MySqlVersionResolver.WithUtf8Mb4(
            "Server=127.0.0.1;Port=1;Database=jev_odds;User=jev;Password=CHANGE_ME;Connection Timeout=1;");
        var version = MySqlVersionResolver.Resolve(connection, Environment.GetEnvironmentVariable("Database__ServerVersion"));
        var options = new DbContextOptionsBuilder<MySqlOddsDbContext>()
            .UseMySql(connection, version)
            .Options;
        return new MySqlOddsDbContext(options);
    }
}
