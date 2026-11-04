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
    DateTime? ReviewedAt = null)   // frontmatter reviewedAt: the user looked at it (set by the app)
{
    /// <summary>An agent stamped this note and the user has not reviewed it since.</summary>
    public bool AgentUnreviewed => AgentBy is not null && (ReviewedAt is null || (AgentAt is DateTime a && ReviewedAt < a));
}

/// <summary>A reference found in note text. Kind is "doc" ([[path]]), "item" (#wiN) or "note" (#nN).</summary>
public sealed record NoteLink(string Kind, string Target, bool Resolved);
