using WorkItems.Infrastructure;
using WorkItems.PublicDTOs;

namespace WorkItems;

/// <summary>
/// One change folder as last read: what its files looked like (name, size and modified time of each, no contents) and the item parsed from
/// them. While the files look the same, and the app did not write there itself, the folder is not read again and the item keeps its object.
/// </summary>
internal sealed class FolderEntry(string dir, bool archived, string fingerprint, Rtm meta, WorkItem item)
{
    internal readonly string Dir = dir;
    internal readonly bool Archived = archived;
    internal readonly string Fingerprint = fingerprint;
    internal readonly Rtm Meta = meta;

    /// <summary>The parsed item; only its number can change afterwards (a provisional number moves when another change appears or goes).</summary>
    internal WorkItem Item = item;

    private WorkItem? final;
    private WorkItem? finalOf;
    private string finalToken = "";
    private int finalEpoch = -1;

    /// <summary>The item with its commit authors standing in as contributors (until the list is edited in the app), kept while the item and the git token are the same.</summary>
    internal WorkItem WithAuthors((string Token, Func<string, IReadOnlyList<string>> AuthorsOf)? git, string token, int epoch)
    {
        if (final is null || !ReferenceEquals(finalOf, Item) || finalToken != token || finalEpoch != epoch)
        {
            final = git is { } g && Item.Assigned is null ? Item with { Contributors = [.. g.AuthorsOf(Item.Folder)] } : Item;
            (finalOf, finalToken, finalEpoch) = (Item, token, epoch);
        }
        return final;
    }
}

/// <summary>What one repository's work items were read from: the open session, the content version and commit token of the assembled list, and every folder's entry.</summary>
internal sealed class RepoCache
{
    internal long Generation;
    internal long Version = -1;
    internal string Git = "";
    internal int GitEpoch = -1;

    /// <summary>How far into OpenSpecStore's record of the app's own writes this cache has looked.</summary>
    internal long Cursor;

    /// <summary>In board order: open changes by folder name, then archived ones.</summary>
    internal List<FolderEntry> Ordered = [];
    internal IReadOnlyList<WorkItem> Items = [];
}
