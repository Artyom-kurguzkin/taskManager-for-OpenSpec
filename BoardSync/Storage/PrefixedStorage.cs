using BoardSync.PublicDTOs;

namespace BoardSync.Storage;

/// <summary>
/// A storage seen from inside a folder of another one: every path is checked as it is anywhere else, and only then put behind the prefix, so a path given here can never name
/// a file outside it (a ".." part, an absolute path or a backslash is refused before the prefix is added). Listings come back relative to the view.
/// </summary>
internal sealed class PrefixedStorage : IBoardStorage
{
    private readonly IBoardStorage _inner;
    private readonly string _prefix;    // "a/b/", never empty

    internal PrefixedStorage(IBoardStorage inner, string prefix)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _prefix = StoragePath.Check(prefix) + "/";      // the prefix is held to the rules of a path: not empty, no "." or ".." part, no backslash, not rooted, no empty part (so no trailing slash)
    }

    public bool ConditionalWrites => _inner.ConditionalWrites;

    public async Task<StoredFile?> Get(string path, CancellationToken ct = default) => await _inner.Get(Inside(path), ct);

    public async Task<StoredFile?> GetIfChanged(string path, string? version, CancellationToken ct = default) => await _inner.GetIfChanged(Inside(path), version, ct);

    public async Task<PutResult> Put(string path, byte[] bytes, PutCondition condition, CancellationToken ct = default) => await _inner.Put(Inside(path), bytes, condition, ct);

    public async Task Delete(string path, CancellationToken ct = default) => await _inner.Delete(Inside(path), ct);

    public async Task<IReadOnlyList<StoredEntry>> List(string prefix, CancellationToken ct = default)
    {
        StoragePath.CheckPrefix(prefix);
        IReadOnlyList<StoredEntry> found = await _inner.List(_prefix + prefix, ct);
        return [.. found.Where(e => e.Path.StartsWith(_prefix, StringComparison.Ordinal)).Select(e => e with { Path = e.Path[_prefix.Length..] })];
    }

    // A bad path comes back as a faulted task, like the providers do.
    private string Inside(string path) => _prefix + StoragePath.Check(path);
}
