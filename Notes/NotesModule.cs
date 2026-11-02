namespace Notes;

/// <summary>Process-wide state: the parsed notes, valid while OpenSpecStore's content version is unchanged.</summary>
internal static class NotesModule
{
    internal static readonly Dictionary<string, (long Version, IReadOnlyList<PublicDTOs.Note> Notes)> Cache = new(StringComparer.OrdinalIgnoreCase);   // per repository
    internal static int Loads;
}
