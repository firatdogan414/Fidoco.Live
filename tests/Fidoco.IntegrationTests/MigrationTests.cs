using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fidoco.IntegrationTests;

/// <summary>Catches a model change that was committed without a migration (migrations are generated for SQL Server).</summary>
public sealed class MigrationTests
{
    public static TheoryData<string> ContextFactories()
    {
        var data = new TheoryData<string>();
        foreach (var type in DesignTimeFactoryTypes())
        {
            data.Add(type.FullName!);
        }

        return data;
    }

    [Fact]
    public void Every_module_with_a_db_context_has_a_design_time_factory()
    {
        Assert.NotEmpty(DesignTimeFactoryTypes());
    }

    [Theory]
    [MemberData(nameof(ContextFactories))]
    public void Model_has_no_pending_changes(string factoryTypeName)
    {
        var factoryType = DesignTimeFactoryTypes().Single(t => t.FullName == factoryTypeName);
        var factory = Activator.CreateInstance(factoryType)!;
        var create = factoryType.GetMethod("CreateDbContext")!;
        using var db = (DbContext)create.Invoke(factory, [Array.Empty<string>()])!;

        Assert.False(db.Database.HasPendingModelChanges(),
            $"{db.GetType().Name} has model changes without a migration. Run: dotnet ef migrations add <Name> -p <module> -s src/Fidoco.Host -c {db.GetType().Name} -o Infrastructure/Migrations");
    }

    private static List<Type> DesignTimeFactoryTypes() =>
        typeof(Program).Assembly.GetReferencedAssemblies()
            .Where(a => a.Name!.StartsWith("Fidoco.Modules.", StringComparison.Ordinal))
            .Select(Assembly.Load)
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDesignTimeDbContextFactory<>)))
            .OrderBy(t => t.FullName)
            .ToList();
}
