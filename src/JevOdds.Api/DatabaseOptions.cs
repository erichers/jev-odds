namespace JevOdds.Api;

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    public string Provider { get; set; } = "Sqlite";

    public string ServerVersion { get; set; } = "";
}
