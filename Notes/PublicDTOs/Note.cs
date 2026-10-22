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
    IReadOnlyList<string> Commits);

/// <summary>A reference found in note text. Kind is "doc" ([[path]]), "item" (#wiN) or "note" (#nN).</summary>
public sealed record NoteLink(string Kind, string Target, bool Resolved);
