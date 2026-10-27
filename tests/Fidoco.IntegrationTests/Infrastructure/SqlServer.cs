using Microsoft.Data.SqlClient;

namespace Fidoco.IntegrationTests.Infrastructure;

/// <summary>
/// Runs only when FIDOCO_TEST_SQLSERVER holds a SQL Server connection string (the CI "sqlserver" job).
/// Filter with: dotnet test --filter Category=SqlServer
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(SqlServerApiFactory.ConnectionStringVariable)))
        {
            Skip = $"Set {SqlServerApiFactory.ConnectionStringVariable} to run SQL Server tests.";
        }
    }
}

/// <summary>The app against a fresh SQL Server database, created by applying every module's migrations.</summary>
public class SqlServerApiFactory() : FidocoApiFactory("SqlServer", CreateDatabaseConnectionString())
{
    public const string ConnectionStringVariable = "FIDOCO_TEST_SQLSERVER";

    private static string CreateDatabaseConnectionString()
    {
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(ConnectionStringVariable))
        {
            InitialCatalog = $"fidoco_test_{Guid.NewGuid():N}",
        };
        return builder.ConnectionString;
    }
}
