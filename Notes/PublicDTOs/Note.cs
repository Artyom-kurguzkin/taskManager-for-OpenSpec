namespace Notes.PublicDTOs;

/// <summary>A markdown note stored at &lt;change&gt;/notes/n&lt;id&gt;.md. <paramref name="Body"/> excludes the frontmatter.</summary>
public sealed record Note(
    int Id,
    int WorkItemId,
    string Path,
    string Title,
    string By,
    DateTime Created,
    DateTime Modified,
    string Body,
    IReadOnlyList<string> ManualLinks,
    IReadOnlyList<string> Commits,
    IReadOnlyList<string> Attachments,   // repo-relative paths under local/ (copied files) or http(s) URLs
    string? AgentBy = null,        // frontmatter agentBy / agentAt: an agent wrote or changed this note, and when
    DateTime? AgentAt = null,
    DateTime? ReviewedAt = null,   // frontmatter reviewedAt: the user looked at it (set by the app)
    int? MergedFrom = null,        // frontmatter mergedFrom / mergedAt: a merge moved this note in from that work item, on that day
    DateTime? MergedAt = null,
    string? Kind = null,           // frontmatter kind: NoteKinds.Decision, null for an ordinary note
    DateTime? ResolvedAt = null)   // frontmatter resolvedAt: the bug was fixed then; null while it is open
{
    /// <summary>A decision: what was decided and why, clear and brief; the worklog emphasises it.</summary>
    public bool IsDecision => Kind == NoteKinds.Decision;
    /// <summary>A bug report; the worklog shows it under its own filter.</summary>
    public bool IsBug => Kind == NoteKinds.Bug;
    /// <summary>A bug nobody has resolved: it counts as an open subtask of its work item.</summary>
    public bool IsOpenBug => IsBug && ResolvedAt is null;
    /// <summary>An agent stamped this note and the user has not reviewed it since.</summary>
    public bool AgentUnreviewed => AgentBy is not null && (ReviewedAt is null || (AgentAt is DateTime a && ReviewedAt < a));
}

/// <summary>The kinds a note can have (frontmatter <c>kind</c>); an ordinary note has none.</summary>
public static class NoteKinds
{
    public const string Decision = "decision";
    public const string Bug = "bug";             // a bug report: what is wrong, how to see it
    public static readonly IReadOnlyList<string> All = [Decision, Bug];
}

/// <summary>A reference found in note text. Kind is "doc" ([[path]]), "item" (#wiN) or "note" (#nN).</summary>
public sealed record NoteLink(string Kind, string Target, bool Resolved);
