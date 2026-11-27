using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BoardSync.Merge;
using BoardSync.PublicDTOs;

namespace BoardSync.Sync;

/// <summary>
/// What the planner does about whole change folders (see <see cref="FolderPass"/>) and about a file that is missing on one side.
/// OpenSpec never deletes a change: a folder that is gone from one side was archived, merged into another item, or renamed (the sync follows it there and carries over
/// the edits made meanwhile), and anything else that is gone is put back. The per-path loop of <see cref="SyncPlanner"/> reads the <see cref="Local"/>, <see cref="Base"/> and
/// <see cref="Remote"/> views of this class: a followed folder's files appear under their new path in them (a "virtual" path), the old paths are removed in the real places.
/// </summary>
internal sealed partial class FolderWork
{
    [GeneratedRegex(@"^ {2}id:\s*(?<v>\d+)\s*$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IdLine();

    [GeneratedRegex(@"^ {2}mergedFrom:\s*\[(?<v>.*)\]\s*$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MergedFromLine();

    [GeneratedRegex(@"^ {2}mergedFolders:\s*\[(?<v>.*)\]\s*$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MergedFoldersLine();

    [GeneratedRegex(@"^ {2}title:\s*(?<v>.*?)\s*$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TitleLine();

    [GeneratedRegex(@"\|\s*merged-from\s*\|\s*#wi(?<v>\d+)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MergedFromLogLine();

    [GeneratedRegex(@"(^|/)notes/n(?<n>\d+)\.md$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NotePath();

    [GeneratedRegex(@"^#?n?(?<n>\d+) -> #?n?\d+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RenumberedText();

    [GeneratedRegex(@"^(?:note|decision|bug) #?n?(?<n>\d+) -> subtask", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ConvertedText();

    [GeneratedRegex(@"^(?<old>specs/\S+?) -> (?<new>specs/\S+)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SpecMovedText();

    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const string ArchivePrefix = "archive/";

    private readonly SyncPlan _plan;
    private readonly SyncStateStore _store;
    private readonly ObjectFetcher _fetcher;
    private readonly CancellationToken _ct;
    private readonly string _device;
    private readonly DateTime _now;

    internal Dictionary<string, LocalFile> RealLocal { get; }
    internal Dictionary<string, ManifestEntry> RealBase { get; }
    internal Dictionary<string, ManifestEntry> RealRemote { get; }

    /// <summary>The views the per-path loop reads: the real ones, with the files of followed folders under their new paths.</summary>
    internal Dictionary<string, LocalFile> Local { get; }
    internal Dictionary<string, ManifestEntry> Base { get; }
    internal Dictionary<string, ManifestEntry> Remote { get; }

    /// <summary>Paths that exist in a view only because a folder was followed there (the file is under its old path on that side).</summary>
    internal HashSet<string> Virtual { get; } = new(StringComparer.Ordinal);

    /// <summary>For a virtual remote path: the real path whose object holds the content (objects are named by path and content).</summary>
    private readonly Dictionary<string, string> _remoteSource = new(StringComparer.Ordinal);

    private readonly Dictionary<string, PlanItem> _items = new(StringComparer.Ordinal);
    private readonly HashSet<string> _localFolders, _remoteFolders;
    private readonly Dictionary<(string Folder, FolderSide Side), FolderResolution> _resolutions = [];
    private readonly Dictionary<string, ChangeFolder> _described = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Folder, FolderSide Side), HashSet<string>> _newLines = [];
    private readonly HashSet<string> _noted = new(StringComparer.Ordinal);
    private readonly List<CarryJob> _carries = [];
    private readonly List<(string Folder, int? Id, string Text, string Other)> _clashNotes = [];
    private readonly HashSet<int> _allocated = [];
    /// <summary>Change folders the pass leaves alone as a whole: one with a file still settling (<see cref="Waiting"/>), one that is incomplete in the storage and not yet here (<see cref="Incomplete"/>), and the sources of a folder held for either reason.</summary>
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);

    private sealed record CarryJob(string From, int? FromId, string FromTitle, string Into, int? IntoId, Dictionary<string, CarryEdit> Edits);

    private FolderWork(SyncPlan plan, SyncStateStore store, ObjectFetcher fetcher, string device, DateTime now, CancellationToken ct,
        Dictionary<string, LocalFile> local, Dictionary<string, ManifestEntry> baseEntries, Dictionary<string, ManifestEntry> remote, IReadOnlyCollection<string>? links)
    {
        _plan = plan; _store = store; _fetcher = fetcher; _device = device; _now = now; _ct = ct;
        RealLocal = local; RealBase = baseEntries; RealRemote = remote;
        Local = new(local, StringComparer.Ordinal);
        Base = new(baseEntries, StringComparer.Ordinal);
        Remote = new(remote, StringComparer.Ordinal);
        _localFolders = FoldersOf(local.Keys);
        foreach (string link in links ?? []) if (FolderOf(link + "/x") is { } linked) _localFolders.Add(linked);      // a folder behind a link is there, only not readable
        _remoteFolders = FoldersOf(remote.Where(e => !e.Value.Deleted).Select(e => e.Key));
    }

    // ---- paths ---------------------------------------------------------------------------------------------------------------

    /// <summary>The change folder a path is in: "x" for x/..., "archive/x" for archive/x/...; null for a file at the top.</summary>
    internal static string? FolderOf(string path)
    {
        int slash = path.IndexOf('/');
        if (slash < 0) return null;
        if (!path.StartsWith(ArchivePrefix, StringComparison.Ordinal)) return path[..slash];
        int second = path.IndexOf('/', ArchivePrefix.Length);
        return second < 0 ? null : path[..second];
    }

    private static HashSet<string> FoldersOf(IEnumerable<string> paths)
    {
        HashSet<string> folders = new(StringComparer.Ordinal);
        foreach (string path in paths) if (FolderOf(path) is { } f) folders.Add(f);
        return folders;
    }

    private static string RestOf(string path, string folder) => path[(folder.Length + 1)..];

    private static bool IsArchived(string folder) => folder.StartsWith(ArchivePrefix, StringComparison.Ordinal);

    // ---- building ------------------------------------------------------------------------------------------------------------

    internal static async Task<FolderWork> Create(SyncPlan plan, SyncStateStore store, ObjectFetcher fetcher, string device, DateTime now, bool whole, CancellationToken ct,
        Dictionary<string, LocalFile> local, Dictionary<string, ManifestEntry> baseEntries, Dictionary<string, ManifestEntry> remote, IReadOnlyCollection<string>? links)
    {
        FolderWork work = new(plan, store, fetcher, device, now, ct, local, baseEntries, remote, links);
        if (!whole)
        {
            await work.HoldFolders();
            await work.FollowFolders();
        }
        return work;
    }

    /// <summary>Change folders held back whole because a file in them is still settling; none of their files is uploaded, downloaded or deleted this pass.</summary>
    internal SortedSet<string> Waiting { get; } = new(StringComparer.Ordinal);

    /// <summary>Change folders in the storage that are incomplete (no live .openspec.yaml, or a merged item whose sources are still live there) and not yet on this computer: not downloaded, not counted.</summary>
    internal SortedSet<string> Incomplete { get; } = new(StringComparer.Ordinal);

    /// <summary>Whether the pass leaves the path alone because its change folder is held (see <see cref="HoldFolders"/>).</summary>
    internal bool IsHeld(string path) => _held.Count > 0 && FolderOf(path) is { } folder && _held.Contains(folder);

    /// <summary>
    /// A change folder is one unit. Held back whole: a folder with a local file that is still settling and differs from what was synced (the pass after it has settled uploads it whole),
    /// and a folder that is incomplete in the storage and not on this computer yet: it has notes but no live .openspec.yaml, or its mergedFrom / mergedFolders names items whose folders are still live there.
    /// The computer that made it uploads the rest on its next pass (the files are newer than the base there, and that computer has the yaml, so this rule does not hold it); until then nothing of it reaches disk here.
    /// </summary>
    private async Task HoldFolders()
    {
        foreach ((string path, LocalFile file) in RealLocal)
        {
            if (!file.Settling || FolderOf(path) is not { } folder) continue;
            string? b = RealBase.TryGetValue(path, out ManifestEntry? be) && !be.Deleted ? be.Hash : null, r = RealRemote.TryGetValue(path, out ManifestEntry? re) && !re.Deleted ? re.Hash : null;
            if (file.Hash == b || file.Hash == r) continue;      // not a change: nothing is being written to it that the storage lacks
            Waiting.Add(folder);
            _held.Add(folder);
        }

        foreach (string folder in _remoteFolders.Order(StringComparer.Ordinal))
        {
            string yaml = folder + "/.openspec.yaml";
            if (RealLocal.ContainsKey(yaml) || RealBase.Any(e => !e.Value.Deleted && e.Key.StartsWith(folder + "/", StringComparison.Ordinal))) continue;      // this computer has the folder, or has taken part of it before: the normal rules
            if (!await IsIncompleteInStorage(folder, yaml)) continue;
            Incomplete.Add(folder);
            _held.Add(folder);
        }
    }

    private async Task<bool> IsIncompleteInStorage(string folder, string yaml)
    {
        if (!RealRemote.TryGetValue(yaml, out ManifestEntry? entry) || entry.Deleted)
            return RealRemote.Any(e => !e.Value.Deleted && e.Key.StartsWith(folder + "/notes/", StringComparison.Ordinal));      // notes without the item's header: only a folder that was left half done looks like this (a bare folder of loose files is somebody's own)
        (int? _, List<int> from, List<string>? names) = Meta(await TextOf(FolderSide.Remote, yaml));
        if (names is not null) return names.Any(n => n != folder && _remoteFolders.Contains(n));
        if (from.Count == 0) return false;
        foreach (string other in _remoteFolders.Where(f => f != folder && !IsArchived(f)))      // an older merge names ids only: the ids of the live folders say whether a source is still there
            if (Meta(await TextOf(FolderSide.Remote, other + "/.openspec.yaml")).Id is int id && from.Contains(id)) return true;
        return false;
    }

    /// <summary>The object path to fetch for a path of the remote view.</summary>
    internal string SourceOf(string path) => _remoteSource.GetValueOrDefault(path, path);

    /// <summary>Adds a plan item for a path (one item per path).</summary>
    internal void Add(PlanItem item)
    {
        _plan.Items.Add(item);
        _items[item.Path] = item;
    }

    private async Task FollowFolders()
    {
        HashSet<string> baseFolders = FoldersOf(RealBase.Where(e => !e.Value.Deleted).Select(e => e.Key));
        List<string> lackLocal = [.. baseFolders.Where(f => !_localFolders.Contains(f))], lackRemote = [.. baseFolders.Where(f => !_remoteFolders.Contains(f))];
        if (lackLocal.Count + lackRemote.Count == 0) return;

        FolderView local = new([.. await Task.WhenAll(_localFolders.Order(StringComparer.Ordinal).Select(f => lackLocal.Count > 0 ? Describe(f, FolderSide.Local) : Task.FromResult(Stub(f))))]);
        FolderView remote = new([.. await Task.WhenAll(_remoteFolders.Where(f => !Incomplete.Contains(f)).Order(StringComparer.Ordinal).Select(f => lackRemote.Count > 0 ? Describe(f, FolderSide.Remote) : Task.FromResult(Stub(f))))]);
        List<ChangeFolder> baseList = [];
        foreach (string f in lackLocal.Concat(lackRemote).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)) baseList.Add(await DescribeBase(f));
        FolderPassResult result = FolderPass.Resolve(local, remote, new FolderView(baseList));
        foreach (ChangeFolder c in local.Folders.Concat(remote.Folders).Concat(baseList)) _described.TryAdd(c.Path, c);
        foreach (FolderResolution r in result.Resolutions) _resolutions[(r.Folder, r.Lacks)] = r;
        foreach (FolderResolution r in result.Resolutions)
            if (r.Destination is not null && _held.Contains(r.Destination)) _held.Add(r.Folder);      // what a held folder took in stays as it is until the folder is let go: nothing is retired for a move that is not whole

        foreach (IGrouping<string, FolderResolution> group in result.Resolutions.GroupBy(r => r.Folder, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            if (_held.Contains(group.Key)) continue;
            FolderResolution? rl = group.FirstOrDefault(r => r.Lacks == FolderSide.Local), rr = group.FirstOrDefault(r => r.Lacks == FolderSide.Remote);
            if (rl is not null && rr is not null) await BothMoved(rl, rr, result.Clashes);
            else if (rl is not null) await OneMoved(rl, FolderSide.Remote);
            else if (rr is not null) await OneMoved(rr, FolderSide.Local);
        }
    }

    private static ChangeFolder Stub(string folder) => new(folder, null, [], null, []);

    private (int? Id, List<int> MergedFrom, List<string>? MergedFolders) Meta(string? yaml)
    {
        if (yaml is null) return (null, [], null);
        int? id = IdLine().Match(yaml) is { Success: true } m ? int.Parse(m.Groups["v"].Value, CultureInfo.InvariantCulture) : null;
        List<int> from = MergedFromLine().Match(yaml) is { Success: true } f ? [.. CarryEdits.SplitFlow(f.Groups["v"].Value).Select(v => int.TryParse(v, CultureInfo.InvariantCulture, out int n) ? n : -1).Where(n => n >= 0)] : [];
        List<string>? folders = MergedFoldersLine().Match(yaml) is { Success: true } d ? [.. CarryEdits.SplitFlow(d.Groups["v"].Value).Select(v => v.Trim('"', '\''))] : null;
        return (id, from, folders);
    }

    private static List<int> LogMerges(string? log) =>
        log is null ? [] : [.. MergedFromLogLine().Matches(log).Select(m => int.Parse(m.Groups["v"].Value, CultureInfo.InvariantCulture))];

    private async Task<ChangeFolder> Describe(string folder, FolderSide side)
    {
        string? yaml = await TextOf(side, folder + "/.openspec.yaml"), log = await TextOf(side, folder + "/events.log");
        (int? id, List<int> from, List<string>? folders) = Meta(yaml);
        return new ChangeFolder(folder, id, from, folders, LogMerges(log));
    }

    private async Task<ChangeFolder> DescribeBase(string folder)
    {
        string path = folder + "/.openspec.yaml";
        string? yaml = BaseText(path) ?? await TextOf(FolderSide.Local, path) ?? await TextOf(FolderSide.Remote, path);
        return new ChangeFolder(folder, Meta(yaml).Id, [], null, []);
    }

    private string? BaseText(string path) =>
        RealBase.TryGetValue(path, out ManifestEntry? e) && !e.Deleted && _store.ReadBase(e.Hash) is { } bytes ? Lenient(bytes) : null;

    private async Task<string?> TextOf(FolderSide side, string path) => await BytesOf(side, path) is { } bytes ? Lenient(bytes) : null;

    private static string Lenient(byte[] bytes) => Utf8.GetString(bytes);

    private async Task<byte[]?> BytesOf(FolderSide side, string path)
    {
        if (side == FolderSide.Local) return RealLocal.TryGetValue(path, out LocalFile? lf) ? lf.Bytes : null;
        if (!RealRemote.TryGetValue(path, out ManifestEntry? e) || e.Deleted) return null;
        if (_store.ReadBase(e.Hash) is { } cached) return cached;      // the base holds this content (under this path or another: a moved folder): no request
        return (await _fetcher.Get(path, e.Hash, _ct)).Bytes();
    }

    // ---- what happened to a folder -------------------------------------------------------------------------------------------

    private int? IdOf(string folder) => _described.TryGetValue(folder, out ChangeFolder? c) ? c.Id : null;

    private void Note(int? id, string folder, string kind, string text) => _plan.Notes.Add(new SyncNote(id, folder, kind, text));

    private async Task OneMoved(FolderResolution res, FolderSide has)
    {
        string f = res.Folder;
        string lacking = res.Lacks == FolderSide.Local ? "this computer" : "the storage";
        switch (res.Fate)
        {
            case FolderFate.Archived or FolderFate.Renamed:
            {
                string to = res.Destination!;
                RewriteBase(f, to);
                bool edited = await MoveSide(has, f, to);
                string what = res.Fate == FolderFate.Archived ? "archive" : "rename";
                if (has == FolderSide.Local)      // the storage followed a move made here: nothing was followed on this computer
                    Note(res.Id, to, res.Fate == FolderFate.Archived ? "followed-archive" : "followed-rename",
                    $"followed the {what} made on another computer ({f} -> {to})" + (edited ? "; merged the edits made here" : ""));
                break;
            }
            case FolderFate.Merged:
            {
                string into = res.Destination!;
                bool carried = await Remove(has, f, into, res.Id);
                if (has == FolderSide.Local) Note(IdOf(into), into, "followed-merge", $"followed the merge made on another computer: #wi{res.Id} ({f}) went into this item" + (carried ? "; its edits made here are carried in" : ""));
                break;
            }
            default:
                _noted.Add(f + "|" + res.Lacks);
                Note(res.Id, f, "put-back-folder", $"put back: the folder was missing on {lacking} and nothing says it moved");
                break;
        }
    }

    private async Task BothMoved(FolderResolution rl, FolderResolution rr, IReadOnlyList<FolderClash> clashes)
    {
        string f = rl.Folder;
        if (rl.Fate == FolderFate.Missing || rr.Fate == FolderFate.Missing) return;      // one side has no trace of it, the other moved it: the folder is nowhere to put back
        string dl = rl.Destination!, dr = rr.Destination!;
        if (string.Equals(dl, dr, StringComparison.Ordinal))
        {
            if (rl.Fate is FolderFate.Archived or FolderFate.Renamed) RewriteBase(f, dl);
            return;
        }
        bool movedL = rl.Fate is FolderFate.Archived or FolderFate.Renamed, movedR = rr.Fate is FolderFate.Archived or FolderFate.Renamed;
        if (movedL && movedR)
        {
            // One folder, one id: keep the archive (the earlier one when both are), or the name that sorts first when both were renamed, and merge the other into it.
            string keep, away;
            FolderClash? clash = clashes.FirstOrDefault(c => c.Folder == f && c.Kind == FolderClashKind.EarlierArchiveKept);
            if (clash is not null) (keep, away) = (clash.Keep!, clash.MergeAway!);
            else if (IsArchived(dl) != IsArchived(dr)) (keep, away) = IsArchived(dl) ? (dl, dr) : (dr, dl);
            else (keep, away) = string.CompareOrdinal(dl, dr) <= 0 ? (dl, dr) : (dr, dl);
            RewriteBase(f, keep);
            bool edited = await MoveSide(away == dl ? FolderSide.Local : FolderSide.Remote, away, keep);
            Note(rl.Id, keep, IsArchived(keep) ? "followed-archive" : "followed-rename",
                $"two computers moved this item to {dl} and {dr}; kept {keep} and merged {away} into it" + (edited ? " (with its edits)" : ""));
            return;
        }
        // Archived and merged, merged into two items, or renamed and merged: both destinations stay, each says where the other went.
        _clashNotes.Add((dl, IdOf(dl), Other(rl.Id, dr, rr.Fate), dr));
        _clashNotes.Add((dr, IdOf(dr), Other(rl.Id, dl, rl.Fate), dl));
    }

    private string Other(int? id, string where, FolderFate fate) =>
        fate == FolderFate.Merged
            ? $"#wi{id} was also merged into #wi{IdOf(where)} on another computer ({where})"
            : fate == FolderFate.Archived
                ? $"#wi{id} was also archived on another computer ({where})"
                : $"#wi{id} was also moved to {where} on another computer";

    private void RewriteBase(string from, string to)
    {
        foreach (string path in Base.Keys.Where(p => p.StartsWith(from + "/", StringComparison.Ordinal)).ToList())
        {
            string target = to + "/" + RestOf(path, from);
            if (!Base.ContainsKey(target)) Base[target] = Base[path];
            Base.Remove(path);
        }
    }

    /// <summary>The live paths of a folder on the side that has it.</summary>
    private List<string> FilesOf(FolderSide side, string folder) =>
        [.. (side == FolderSide.Local ? Local.Keys : Remote.Where(e => !e.Value.Deleted).Select(e => e.Key)).Where(p => p.StartsWith(folder + "/", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

    /// <summary>Takes the folder's files off the side that has it (here: to the trash; in the storage: a marker, the objects stay) and returns each one's old path.</summary>
    private void Retire(FolderSide side, string path)
    {
        PlanItem item = new(path);
        if (side == FolderSide.Local)
        {
            item.Op = LocalOp.Delete;
            item.ExpectedLocalHash = Local[path].Hash;
            Local.Remove(path);
            _plan.DeletedHere.Add(path);
        }
        else
        {
            ManifestEntry entry = Remote[path];
            item.NewEntry = new ManifestEntry { Hash = entry.Hash, Clock = SyncClock.After(SyncClock.Stamp(_now, _device), entry.Clock, _device), Deleted = true, History = [.. entry.History] };
            Remote.Remove(path);
            _plan.DeletedInStorage.Add(path);
        }
        Add(item);
    }

    /// <summary>Archived or renamed on the other side: every file of the folder goes to the same place under the new folder, here as a virtual path. Returns whether any of them was edited since the last sync.</summary>
    private async Task<bool> MoveSide(FolderSide side, string from, string to)
    {
        bool edited = false;
        foreach (string path in FilesOf(side, from))
        {
            string target = to + "/" + RestOf(path, from);
            bool free = side == FolderSide.Local ? !Local.ContainsKey(target) : !(Remote.TryGetValue(target, out ManifestEntry? t) && !t.Deleted);
            string? hash = side == FolderSide.Local ? Local[path].Hash : Remote[path].Hash;
            edited |= !RealBase.TryGetValue(path, out ManifestEntry? was) || was.Deleted || was.Hash != hash;
            if (free)
            {
                Virtual.Add(target);
                if (side == FolderSide.Local) Local[target] = Local[path];
                else
                {
                    Remote[target] = Remote[path];
                    _remoteSource[target] = SourceOf(path);
                }
            }
            Retire(side, path);
        }
        await Task.CompletedTask;
        return edited;
    }

    /// <summary>Merged elsewhere into <paramref name="into"/>: the old copy goes (trash or marker); what was edited since the merge is carried into the merged item. Returns whether anything was carried.</summary>
    private async Task<bool> Remove(FolderSide side, string from, string into, int? id)
    {
        Dictionary<string, CarryEdit> edits = new(StringComparer.Ordinal);
        foreach (string path in FilesOf(side, from))
        {
            string? hash = side == FolderSide.Local ? Local[path].Hash : Remote[path].Hash;
            RealBase.TryGetValue(path, out ManifestEntry? was);
            if (was is { Deleted: false } && was.Hash == hash) { Retire(side, path); continue; }       // untouched since the merge: nothing to carry
            string? mine = await TextStrict(side, path);
            string? before = was is { Deleted: false } && _store.ReadBase(was.Hash) is { } b ? Lenient(b) : null;
            if (mine is not null && !(was is { Deleted: false } && before is null)) edits[RestOf(path, from)] = new CarryEdit(before, mine);
            Retire(side, path);
        }
        if (edits.Count == 0) return false;
        string title = TitleLine().Match(edits.TryGetValue(".openspec.yaml", out CarryEdit? y) ? y.Base ?? y.Mine ?? "" : BaseText(from + "/.openspec.yaml") ?? "") is { Success: true } m ? m.Groups["v"].Value.Trim('"', '\'') : "";
        _carries.Add(new CarryJob(from, id, title, into, IdOf(into), edits));
        return true;
    }

    private async Task<string?> TextStrict(FolderSide side, string path)
    {
        byte[]? bytes = await BytesOf(side, path);
        if (bytes is null) return null;
        try { return StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { return null; }
    }

    // ---- a file that is missing ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether the removal of <paramref name="path"/> on the side that lacks it is explained: its folder moved (archived, merged, renamed), or that side's event log has a line
    /// newer than the last sync that says the file went (a note renumbered or converted, a spec removed or moved). Anything else is not a removal: the file is put back.
    /// </summary>
    internal async Task<bool> RemovalExplained(string path, FolderSide lacking)
    {
        if (FolderOf(path) is not { } folder) return false;
        if (_resolutions.TryGetValue((folder, lacking), out FolderResolution? res)) return res.Fate != FolderFate.Missing;
        if (!_localFolders.Contains(folder) || !_remoteFolders.Contains(folder)) return false;
        string rest = RestOf(path, folder);
        foreach (string line in await NewLines(folder, lacking))
        {
            string[] parts = line.Split(" | ", 4);
            if (parts.Length < 4) continue;
            string text = parts[3].Trim();
            switch (parts[2].Trim())
            {
                case "renumbered" when RenumberedText().Match(text) is { Success: true } r && rest == $"notes/n{r.Groups["n"].Value}.md":
                case "converted" when ConvertedText().Match(text) is { Success: true } c && rest == $"notes/n{c.Groups["n"].Value}.md":
                case "spec-removed" when text == rest && rest.StartsWith("specs/", StringComparison.Ordinal):
                    return true;
                case "spec-moved" when SpecMovedText().Match(text) is { Success: true } s && rest.StartsWith(s.Groups["old"].Value + "/", StringComparison.Ordinal):
                    return true;
            }
        }
        return false;
    }

    /// <summary>The lines the side's event log has that the log of the last sync did not.</summary>
    private async Task<HashSet<string>> NewLines(string folder, FolderSide side)
    {
        if (_newLines.TryGetValue((folder, side), out HashSet<string>? known)) return known;
        string log = folder + "/events.log";
        string? now = await TextOf(side, log), before = BaseText(log);
        HashSet<string> had = [.. Lines(before)];
        HashSet<string> added = [.. Lines(now).Where(l => !had.Contains(l))];
        return _newLines[(folder, side)] = added;
    }

    private static IEnumerable<string> Lines(string? text) => (text ?? "").Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0);

    /// <summary>A file was missing and nothing explained it: the report says so on its item (a folder put back whole says it once, for the folder).</summary>
    internal async Task PutBack(string path, FolderSide lacking)
    {
        if (FolderOf(path) is not { } folder) return;
        if (_noted.Contains(folder + "|" + lacking)) return;
        string where = lacking == FolderSide.Local ? "this computer" : "the storage";
        int? id = IdOf(folder);
        if (id is null && await TextOf(FolderSide.Local, folder + "/.openspec.yaml") is { } yaml) id = Meta(yaml).Id;
        if (id is null && await TextOf(FolderSide.Remote, folder + "/.openspec.yaml") is { } remoteYaml) id = Meta(remoteYaml).Id;
        Note(id, folder, "put-back-file", $"put back {RestOf(path, folder)}: it was missing on {where} and nothing says it was removed on purpose");
    }

    // ---- the end of the pass -------------------------------------------------------------------------------------------------

    /// <summary>The bytes the plan leaves at a path, or null when it leaves none.</summary>
    private byte[]? FinalBytes(string path)
    {
        if (_plan.FinalContent.TryGetValue(path, out Func<byte[]>? content)) return content();
        if (_plan.FinalFiles.TryGetValue(path, out string? file) && File.Exists(file)) return File.ReadAllBytes(file);
        return null;
    }

    /// <summary>Makes the plan leave exactly these bytes at the path, on this computer and in the storage, whatever it planned before.</summary>
    private void SetFinal(string path, byte[] bytes)
    {
        string hash = SyncModels.HashOf(bytes);
        string? l = RealLocal.TryGetValue(path, out LocalFile? lf) ? lf.Hash : null;
        ManifestEntry? re = RealRemote.GetValueOrDefault(path);
        string? r = re is { Deleted: false } ? re.Hash : null;
        if (!_items.TryGetValue(path, out PlanItem? item)) Add(item = new PlanItem(path));
        _plan.FinalContent[path] = () => bytes;
        _plan.FinalFiles.Remove(path);
        item.ExpectedLocalHash = l;
        item.Probe = false;
        if (hash != l)
        {
            item.Op = LocalOp.Write;
            item.WriteBytes = bytes;
            item.WriteFrom = null;
            item.KeepOld = true;
            if (!_plan.Downloaded.Contains(path) && !_plan.Merged.Contains(path)) (hash == r ? _plan.Downloaded : _plan.Merged).Add(path);       // what the storage holds, brought here: a download
        }
        else { item.Op = LocalOp.None; item.WriteBytes = null; item.WriteFrom = null; }
        if (hash != r)
        {
            ManifestEntry? current = re ?? Remote.GetValueOrDefault(path);
            List<string> history = current is null || current.Hash == hash ? [.. current?.History ?? []] : [.. new[] { current.Hash }.Concat(current.History).Distinct().Take(SyncModels.HistoryLimit)];
            item.NewEntry = new ManifestEntry { Hash = hash, Clock = SyncClock.After(SyncClock.Stamp(_now, _device), current?.Clock, _device), Deleted = false, History = history };
            item.Upload = () => bytes;
            if (!_plan.Uploaded.Contains(path) && !_plan.Merged.Contains(path)) (hash == l ? _plan.Uploaded : _plan.Merged).Add(path);         // what is here, put in the storage: an upload
        }
        else { item.NewEntry = null; item.Upload = null; }
    }

    private int NextNoteId()
    {
        int max = 0;
        foreach (string path in RealLocal.Keys.Concat(RealRemote.Keys).Concat(RealBase.Keys))
            if (NotePath().Match(path) is { Success: true } m && int.TryParse(m.Groups["n"].Value, CultureInfo.InvariantCulture, out int n)) max = Math.Max(max, n);
        foreach (int a in _allocated) max = Math.Max(max, a);
        _allocated.Add(max + 1);
        return max + 1;
    }

    /// <summary>After the per-path loop: every followed file is written where it went, edits are carried into merged items, and notes naming the other destination are written.</summary>
    internal async Task Finish()
    {
        foreach (string path in Virtual.Order(StringComparer.Ordinal))
            if (FinalBytes(path) is { } bytes) SetFinal(path, bytes);

        foreach (CarryJob job in _carries)
        {
            Dictionary<string, string> written = [];
            string? Z(string rest) => FinalBytes(job.Into + "/" + rest) is { } b ? Lenient(b) : null;
            bool taken(int n) => RealLocal.Keys.Concat(RealRemote.Keys).Concat(RealBase.Keys).Any(p => NotePath().Match(p) is { Success: true } m && m.Groups["n"].Value == n.ToString(CultureInfo.InvariantCulture) && !p.StartsWith(job.From + "/", StringComparison.Ordinal));
            CarryOutcome outcome = CarryEdits.Carry(job.FromId ?? 0, job.FromTitle, job.Edits, Z, taken, NextNoteId, _now);
            foreach ((string rest, string text) in outcome.Files) SetFinal(job.Into + "/" + rest, Utf8.GetBytes(text));
            if (outcome.Files.Count > 0)
                Note(job.IntoId, job.Into, "carried", $"carried the edits made on another computer to #wi{job.FromId} ({job.From}) into this item after the merge" + (outcome.WroteNote ? "; what could not be applied is in a note" : ""));
        }

        foreach ((string folder, int? id, string text, string other) in _clashNotes)
        {
            int n = NextNoteId();
            string note = $"---\nid: {n}\nby: Sync\ncreated: {_now.ToString("s", CultureInfo.InvariantCulture)}\n---\n# {text}\n\n{text}. Both items are kept; nothing was dropped. See {other}.\n";
            SetFinal($"{folder}/notes/n{n}.md", Utf8.GetBytes(note));
            Note(id, folder, "noted", text);
        }
        await Task.CompletedTask;
    }
}
