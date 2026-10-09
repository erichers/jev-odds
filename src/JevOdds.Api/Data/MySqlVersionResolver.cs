using Microsoft.EntityFrameworkCore;

namespace JevOdds.Api.Data;

public static class MySqlVersionResolver
{
    public static ServerVersion Resolve(string connectionString, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)
            && !configured.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return ServerVersion.Parse(configured.Trim());
        }

        try
        {
            return ServerVersion.AutoDetect(connectionString);
        }
        catch (Exception)
        {
            return new MySqlServerVersion(new Version(5, 7, 39));
        }
    }

    public static string WithUtf8Mb4(string connectionString)
    {
        if (connectionString.Contains("CharSet=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Character Set=", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        return connectionString.Trim().TrimEnd(';') + ";CharSet=utf8mb4;";
    }
}
