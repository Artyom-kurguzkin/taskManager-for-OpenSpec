using Notes.PublicDTOs;
using WorkItems.PublicDTOs;

namespace TaskManager.AppState;

/// <summary>
/// What a page reads of one item on demand: its description (proposal.md), its events and its notes. Each is kept with the look of the file(s) it was read from (size and modified time, git's index rule)
/// and is read again only when that differs; a file whose time is within <see cref="Stamps.Slack"/> of the moment it was read is "racy" (a later edit can share its time and size) and is read again every time until it settles.
/// </summary>
internal sealed class ItemBodies
{
    public Read<string>? Description;
    public Read<IReadOnlyList<WorkItemEvent>>? Events;
    public Read<NotesFolderRead>? Notes;
}

/// <summary>A body as read: the repository session and look it was read at, the newest modified time it came from, when it was read, and the value.</summary>
internal sealed record Read<T>(long Generation, string Stamp, long Newest, DateTime ReadAt, T Value)
{
    /// <summary>Still what the files say now: same session, same look, and not racy.</summary>
    internal bool Holds(long generation, string stamp) => Generation == generation && Stamp == stamp && !Stamps.Racy(Newest, ReadAt);
}

internal static class Stamps
{
    /// <summary>How close to the read a file's modified time may be and still be trusted: a file system stamps with a clock that ticks about every 16 ms.</summary>
    internal static readonly long Slack = TimeSpan.FromMilliseconds(20).Ticks;

    internal static bool Racy(long newestTicks, DateTime readAt) => newestTicks >= readAt.Ticks - Slack;

    /// <summary>The look of a file: its path, size and modified time (from the directory entry, nothing opened), and the modified time alone; "-" when it is not there.</summary>
    internal static (string Stamp, long Newest) Of(string path)
    {
        FileInfo f = new(path);
        return f.Exists ? ($"{f.FullName}|{f.Length}|{f.LastWriteTimeUtc.Ticks}", f.LastWriteTimeUtc.Ticks) : (path + "|-", 0);
    }
}

/// <summary>
/// The bodies the app holds, for at most <see cref="Bound"/> items: the ones used last. An item that has to make room is dropped with everything read of it, so memory stays bounded
/// however many items are opened. (The item page needs no dispose hook for it: closing the page leaves its item the next to go once others are opened.)
/// </summary>
internal sealed class BodyCache
{
    internal const int Bound = 64;

    private readonly object gate = new();
    private readonly Dictionary<(string Root, int Id), LinkedListNode<((string Root, int Id) Key, ItemBodies Bodies)>> nodes = [];
    private readonly LinkedList<((string Root, int Id) Key, ItemBodies Bodies)> recent = new();   // first = most recently used

    /// <summary>How many items are held.</summary>
    internal int Count { get { lock (gate) return nodes.Count; } }

    /// <summary>The bodies of the item (an empty set when it has none yet), now the most recently used; the least recently used item is dropped when there are more than <see cref="Bound"/>.</summary>
    internal ItemBodies Of(string root, int id)
    {
        lock (gate)
        {
            if (nodes.TryGetValue((root, id), out var node)) { recent.Remove(node); recent.AddFirst(node); return node.Value.Bodies; }
            ItemBodies made = new();
            nodes[(root, id)] = recent.AddFirst(((root, id), made));
            while (nodes.Count > Bound) { nodes.Remove(recent.Last!.Value.Key); recent.RemoveLast(); }
            return made;
        }
    }

    /// <summary>Forgets everything held of one repository (of all without a root).</summary>
    internal void Clear(string? root)
    {
        lock (gate)
            foreach (var key in nodes.Keys.Where(k => root is null || string.Equals(k.Root, root, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                recent.Remove(nodes[key]);
                nodes.Remove(key);
            }
    }
}
