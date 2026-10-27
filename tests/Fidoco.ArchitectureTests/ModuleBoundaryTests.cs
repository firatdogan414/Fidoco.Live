using System.Reflection;
using Fidoco.Contracts;
using Fidoco.SharedKernel.Observability;
using NetArchTest.Rules;

namespace Fidoco.ArchitectureTests;

/// <summary>
/// Enforces the module rules from ARCHITECTURE.md: modules reference only SharedKernel and Contracts,
/// never each other's internals; Contracts and SharedKernel stay free of module code.
/// </summary>
public sealed class ModuleBoundaryTests
{
    private const string ModulePrefix = "Fidoco.Modules.";
    private const string VerticalPrefix = "Fidoco.Verticals.";

    private static readonly Assembly SharedKernel = typeof(FidocoTelemetry).Assembly;
    private static readonly Assembly Contracts = typeof(ContractsAssembly).Assembly;

    public static TheoryData<string> ModuleNames()
    {
        var data = new TheoryData<string>();
        foreach (var assembly in ModuleAssemblies())
        {
            data.Add(assembly.GetName().Name!);
        }

        return data;
    }

    [Fact]
    public void All_six_core_modules_are_loaded()
    {
        Assert.Equal(
            ["Catalog", "Identity", "Notifications", "Payments", "Playback", "Tenants"],
            ModuleAssemblies().Select(a => a.GetName().Name![ModulePrefix.Length..]).Order());
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_does_not_reference_other_modules(string moduleName)
    {
        var assembly = Load(moduleName);
        var forbidden = assembly.GetReferencedAssemblies()
            .Select(r => r.Name!)
            .Where(n => n.StartsWith(ModulePrefix, StringComparison.Ordinal) || n.StartsWith(VerticalPrefix, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(forbidden);
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_types_do_not_depend_on_other_module_namespaces(string moduleName)
    {
        var others = ModuleAssemblies().Select(a => a.GetName().Name!).Where(n => n != moduleName).ToArray();

        var result = Types.InAssembly(Load(moduleName))
            .ShouldNot().HaveDependencyOnAny(others)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Only_the_module_entry_point_is_public(string moduleName)
    {
        var shortName = moduleName[ModulePrefix.Length..];
        var publicTypes = Load(moduleName).GetExportedTypes()
            // EF Core generates migrations as public classes; they are tooling artefacts, not module API.
            .Where(t => !t.Namespace!.EndsWith(".Migrations", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToList();

        Assert.Equal([$"{moduleName}.{shortName}Module"], publicTypes);
    }

    [Fact]
    public void Contracts_reference_only_shared_kernel()
    {
        var fidocoReferences = Contracts.GetReferencedAssemblies()
            .Select(r => r.Name!)
            .Where(n => n.StartsWith("Fidoco.", StringComparison.Ordinal))
            .ToList();

        Assert.All(fidocoReferences, n => Assert.Equal(SharedKernel.GetName().Name, n));
    }

    [Fact]
    public void Shared_kernel_references_no_fidoco_assembly()
    {
        var fidocoReferences = SharedKernel.GetReferencedAssemblies()
            .Select(r => r.Name!)
            .Where(n => n.StartsWith("Fidoco.", StringComparison.Ordinal));

        Assert.Empty(fidocoReferences);
    }

    [Fact]
    public void Verticals_reference_only_contracts_and_shared_kernel()
    {
        var allowed = new[] { SharedKernel.GetName().Name, Contracts.GetName().Name };
        foreach (var vertical in LoadAll(VerticalPrefix))
        {
            var fidocoReferences = vertical.GetReferencedAssemblies()
                .Select(r => r.Name!)
                .Where(n => n.StartsWith("Fidoco.", StringComparison.Ordinal));
            Assert.All(fidocoReferences, n => Assert.Contains(n, allowed));
        }
    }

    private static IEnumerable<Assembly> ModuleAssemblies() => LoadAll(ModulePrefix);

    private static IEnumerable<Assembly> LoadAll(string prefix)
    {
        // The host references every module and vertical, so walking its references finds them all.
        var host = typeof(Program).Assembly;
        return host.GetReferencedAssemblies()
            .Where(r => r.Name!.StartsWith(prefix, StringComparison.Ordinal))
            .Select(Assembly.Load)
            .OrderBy(a => a.GetName().Name)
            .ToList();
    }

    private static Assembly Load(string name) => Assembly.Load(new AssemblyName(name));

    private static string Describe(TestResult result) =>
        result.IsSuccessful ? string.Empty : "Violations: " + string.Join(", ", result.FailingTypeNames ?? []);
}
