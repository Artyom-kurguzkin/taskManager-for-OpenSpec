namespace WorkItems.PublicDTOs;

/// <summary>One change folder parsed from disk: where it is, the number it stores (null = none yet) and the item read from it, as it is before it gets its number, its commit authors and its status fallback.</summary>
public sealed class FolderRead
{
    public required string Dir { get; init; }
    public required bool Archived { get; init; }
    public int? StoredId { get; init; }
    public required WorkItem Item { get; init; }
    internal Infrastructure.Rtm Meta { get; init; } = null!;
}

/// <summary>The board as read: the items in board order, and the folders they were read from (the same order).</summary>
public sealed record BoardRead(IReadOnlyList<WorkItem> Items, IReadOnlyList<FolderRead> Folders);
