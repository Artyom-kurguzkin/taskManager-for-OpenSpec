namespace WorkItems;

/// <summary>Process-wide state. Only the archive step is swappable (tests replace the CLI call).</summary>
internal static class WorkItemsModule
{
    internal static Func<string, (bool Success, string Message)> Archive = Infrastructure.OpenSpecCli.Archive;
}
