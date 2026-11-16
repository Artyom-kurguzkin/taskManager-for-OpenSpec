namespace Notes.PublicDTOs;

/// <summary>One change's notes/ folder as read from disk: the files read and the notes parsed from them. Opaque to the host, which keeps it while the folder's files look the same and hands it back to the module.</summary>
public sealed class NotesFolderRead
{
    internal List<Queries.Raw> Raws { get; init; } = [];
    internal List<Note> Notes { get; set; } = [];
}

/// <summary>The notes of the repository after the collisions are settled, and the highest note number seen anywhere (file names and frontmatter alike); Wrote says whether settling wrote files (the host then takes the content version after those writes as its own).</summary>
public sealed record NotesRead(IReadOnlyList<Note> Notes, int MaxId, bool Wrote = false);
