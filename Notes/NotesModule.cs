namespace Notes;

/// <summary>Process-wide state: the parsed notes, valid while OpenSpecStore's content version is unchanged.</summary>
internal static class NotesModule
{
    internal static (long Version, IReadOnlyList<PublicDTOs.Note> Notes)? Cache;
    internal static int Loads;
}
