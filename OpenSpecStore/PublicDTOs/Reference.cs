namespace OpenSpecStore.PublicDTOs;

/// <summary>
/// One reference found in a text. <paramref name="Kind"/> is "item" (#wiN), "note" (#nN) or "doc" ([[path]]); <paramref name="Target"/> is the number as written, or the trimmed document path;
/// <paramref name="Number"/> is the number of an item or note, 0 for a document. <paramref name="Start"/> and <paramref name="Length"/> are where it stands in the text.
/// </summary>
public sealed record Reference(string Kind, string Target, int Number, int Start, int Length);
