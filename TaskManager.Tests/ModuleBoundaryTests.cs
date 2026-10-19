using System.Reflection;

namespace TaskManager.Tests;

/// <summary>
/// Defense-in-depth for the module rules (the real enforcement is the <c>internal</c> keyword).
/// Two things may be public per module: the single static entry point <c>{Module}Api</c>, and plain data
/// types under <c>{Module}.PublicDTOs</c>. Modules never reference the host.
/// </summary>
public sealed class ModuleBoundaryTests
{
    public static TheoryData<string> Modules => ["OpenSpecStore", "GitHistory", "Features", "WorkItems", "Notes", "TestResults"];

    private static Assembly Load(string module) => Assembly.Load(module);

    [Theory(DisplayName = "A module exposes only its Api class and its PublicDTOs")]
    [MemberData(nameof(Modules))]
    public void PublicSurfaceIsApiPlusPublicDtos(string module)
    {
        Type[] stray = Load(module).GetExportedTypes()
            .Where(t => t.FullName != $"{module}.{module}Api" && t.Namespace != $"{module}.PublicDTOs")
            .ToArray();
        Assert.Empty(stray);
    }

    [Theory(DisplayName = "A module's Api is a public static class")]
    [MemberData(nameof(Modules))]
    public void ApiIsStatic(string module)
    {
        Type? api = Load(module).GetType($"{module}.{module}Api");
        Assert.NotNull(api);
        Assert.True(api.IsAbstract && api.IsSealed, $"{api.Name} must be static");
    }

    [Theory(DisplayName = "A module never references the host app")]
    [MemberData(nameof(Modules))]
    public void DoesNotReferenceHost(string module) =>
        Assert.DoesNotContain(Load(module).GetReferencedAssemblies(), a => a.Name == "TaskManager");

    [Theory(DisplayName = "Public DTOs are thin data types")]
    [MemberData(nameof(Modules))]
    public void PublicDtosAreThin(string module)
    {
        foreach (Type t in Load(module).GetExportedTypes().Where(t => t.Namespace == $"{module}.PublicDTOs" && !t.IsEnum && !t.IsInterface))
        {
            MethodInfo[] behaviour = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && m.Name is not ("ToString" or "Equals" or "GetHashCode" or "Deconstruct" or "<Clone>$" or "PrintMembers"))
                .ToArray();
            Assert.True(behaviour.Length == 0, $"{t.Name} has behaviour: {string.Join(", ", behaviour.Select(m => m.Name))}");
        }
    }
}
