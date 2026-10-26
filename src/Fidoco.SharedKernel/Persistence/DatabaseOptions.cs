namespace Fidoco.SharedKernel.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>"Sqlite" (local development, tests) or "SqlServer" (production).</summary>
    public string Provider { get; set; } = DatabaseProviders.Sqlite;

    public string ConnectionString { get; set; } = "Data Source=App_Data/fidoco.db";

    /// <summary>Apply EF Core migrations at startup (SQL Server). SQLite schemas are always created from the model.</summary>
    public bool MigrateOnStartup { get; set; }
}

public static class DatabaseProviders
{
    public const string Sqlite = "Sqlite";
    public const string SqlServer = "SqlServer";
}
