namespace BoardSync.Merge;

/// <summary>One keyed piece of a file: a YAML key with its value lines, or a subtask. <c>Value</c> is what is compared, <c>Text</c> is what is written.</summary>
internal sealed record Entry(string Key, string Value, string Text);

/// <summary>Three-way merge of keyed entries, in the order of the local file; what only the remote added goes in after the entry it followed there.</summary>
internal static class Weave
{
    internal static List<Entry> Merge(List<Entry> baseEntries, List<Entry> local, List<Entry> remote, Func<Entry?, Entry?, Entry?, Entry?> decide)
    {
        Dictionary<string, Entry> b = baseEntries.ToDictionary(e => e.Key), l = local.ToDictionary(e => e.Key), r = remote.ToDictionary(e => e.Key);
        // A linked list with an index by key: finding an entry and inserting after it cost nothing, so a file of 100000 entries is not quadratic.
        LinkedList<Entry> result = new();
        Dictionary<string, LinkedListNode<Entry>> nodes = new(StringComparer.Ordinal);
        foreach (Entry e in local)
            if (decide(b.GetValueOrDefault(e.Key), e, r.GetValueOrDefault(e.Key)) is { } kept) nodes.TryAdd(kept.Key, result.AddLast(kept));

        LinkedListNode<Entry>? at = null;       // null: before the first entry
        foreach (Entry e in remote)
        {
            if (l.ContainsKey(e.Key))
            {
                if (nodes.TryGetValue(e.Key, out LinkedListNode<Entry>? found)) at = found;
                continue;
            }
            if (decide(b.GetValueOrDefault(e.Key), null, e) is not { } added) continue;
            // what the local side added at the same spot goes first, then the remote's
            for (LinkedListNode<Entry>? next = at is null ? result.First : at.Next; next is not null && !b.ContainsKey(next.Value.Key) && !r.ContainsKey(next.Value.Key); next = at.Next) at = next;
            at = at is null ? result.AddFirst(added) : result.AddAfter(at, added);
            nodes.TryAdd(added.Key, at);
        }
        return [.. result];
    }

    /// <summary>The usual rule: equal sides, or only one side changed against the base, or (both changed) <paramref name="combine"/>, or the newer side wins and the clash is reported.</summary>
    internal static Entry? Decide(Entry? b, Entry? l, Entry? r, MergeContext ctx, string what, Func<Entry, string> show, Func<Entry?, Entry, Entry, Entry?>? combine = null)
    {
        if (Same(l, r)) return l;
        if (Same(b, l)) return r;
        if (Same(b, r)) return l;
        if (l is not null && r is not null && combine?.Invoke(b, l, r) is { } merged) return merged;
        Entry? kept = ctx.Newer(l, r), dropped = ctx.Newer(r, l);
        ctx.Report(what, kept is null ? "(removed)" : show(kept), dropped is null ? "(removed)" : show(dropped));
        return kept;
    }

    private static bool Same(Entry? a, Entry? b) => a is null ? b is null : b is not null && a.Value == b.Value;

    /// <summary>Makes keys unique within one file: the second "L:" becomes "L:#2".</summary>
    internal static string Unique(Dictionary<string, int> seen, string key)
    {
        int n = seen[key] = seen.GetValueOrDefault(key) + 1;
        return n == 1 ? key : $"{key}#{n}";
    }
}
