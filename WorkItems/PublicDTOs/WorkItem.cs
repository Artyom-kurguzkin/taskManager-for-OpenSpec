namespace WorkItems.PublicDTOs;

/// <summary>An OpenSpec change seen as a work item. <paramref name="Path"/> is the change folder (active or archived).</summary>
public sealed record WorkItem(
    int Id,
    string Folder,
    string Path,
    string Title,
    string Status,
    string Priority,
    IReadOnlyList<string> Contributors,
    IReadOnlyList<FeatureLink> Features,
    IReadOnlyList<Subtask> Subtasks,
    IReadOnlyDictionary<string, string> Lanes,
    bool Archived,
    DateTime Created,
    DateTime Modified,
    string? Warning,
    bool Hidden = false);

/// <summary>A capability the work item touches. Locked = it comes from the change's own specs/ folder.</summary>
public sealed record FeatureLink(string Path, bool Locked);

/// <summary>One checkbox line of tasks.md.</summary>
public sealed record Subtask(string Number, string Text, bool Done, string Group);
