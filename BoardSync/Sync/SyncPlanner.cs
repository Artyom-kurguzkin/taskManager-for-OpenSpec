using System.Text;
using BoardSync.PublicDTOs;
using BoardSync.Storage;

namespace BoardSync.Sync;

internal enum LocalOp { None, Write, Delete }

/// <summary>What to do about one path: the manifest entry it gets (null = unchanged), the object to upload, and what to do to the local file.</summary>
internal sealed class PlanItem(string path)
{
    internal string Path { get; } = path;
    internal ManifestEntry? NewEntry { get; set; }
    /// <summary>The bytes to upload, read when the upload happens (a large local file is not held in memory until then).</summary>
    internal Func<byte[]>? Upload { get; set; }
    internal LocalOp Op { get; set; }
    internal byte[]? WriteBytes { get; set; }
    /// <summary>A download: the file holding the content to write (a merge result is in <see cref="WriteBytes"/> instead). It is copied into place, never read whole into memory.</summary>
    internal FetchedObject? WriteFrom { get; set; }
    /// <summary>The hash of what a write puts in place.</summary>
    internal string WriteHash => WriteFrom?.Hash ?? SyncModels.HashOf(WriteBytes!);
    /// <summary>What the local file must still hash to when the write happens (null: it must not exist); anything else means someone edited it meanwhile.</summary>
    internal string? ExpectedLocalHash { get; set; }
    /// <summary>The local content that a first connection replaces with the storage's, to be copied into the backup folder first.</summary>
    internal byte[]? BackupOf { get; set; }
    /// <summary>The local bytes that a write overwrites although they exist nowhere else (a non-text clash the storage wins), to be copied into the trash first. A delete needs none: the file itself is moved.</summary>
    internal byte[]? TrashOf { get; set; }
    /// <summary>An outside change uploaded as it is: the engine puts its object first, and when the storage refuses (it is already there) this path has had this content before, so it is stale.</summary>
    internal bool Probe { get; set; }
    /// <summary>A download: the local bytes it replaces (when they differ) are copied into the trash first, and the copy is reported as replaced.</summary>
    internal bool KeepOld { get; set; }
}

internal sealed class SyncPlan
{
    internal List<PlanItem> Items { get; } = [];
    /// <summary>What the sync did on its own that an item should say (see <see cref="SyncNote"/>).</summary>
    internal List<SyncNote> Notes { get; } = [];
    /// <summary>The content every live path ends with, read only when a base copy has to be written.</summary>
    internal Dictionary<string, Func<byte[]>> FinalContent { get; } = new(StringComparer.Ordinal);
    /// <summary>The paths whose final content is a downloaded file: the base copy is made by copying it.</summary>
    internal Dictionary<string, string> FinalFiles { get; } = new(StringComparer.Ordinal);
    internal List<string> Uploaded { get; } = [];
    internal List<string> Downloaded { get; } = [];
    internal List<string> Merged { get; } = [];
    internal List<string> DeletedHere { get; } = [];
    internal List<string> DeletedInStorage { get; } = [];
    internal List<SyncConflict> Conflicts { get; } = [];
    internal List<string> BackedUp { get; } = [];
    internal List<string> Stale { get; } = [];
    /// <summary>Files left alone because they are larger than one stored file may be (local ones, and objects in the storage).</summary>
    internal List<string> TooLarge { get; } = [];
    /// <summary>Files left for the next pass because they were written too recently (the settle window).</summary>
    internal List<string> Settling { get; } = [];
    /// <summary>Change folders left whole for the next pass because a file in them is still settling.</summary>
    internal List<string> WaitingFolders { get; } = [];
    /// <summary>Change folders in the storage that are incomplete and not on this computer yet: not downloaded.</summary>
    internal List<string> IncompleteFolders { get; } = [];
    internal List<string> Trashed { get; } = [];
    /// <summary>Copies of local files that a download replaced (in the trash), not files the sync removed.</summary>
    internal List<string> Replaced { get; } = [];
    /// <summary>Paths the plan left alone because a link on this computer is in the way (the link itself, or something the storage has under it).</summary>
    internal HashSet<string> LinkedPaths { get; } = new(StringComparer.Ordinal);
    internal List<string> SkippedLinks { get; set; } = [];
    internal string? TrashFolder { get; set; }
    internal string? BackupFolder { get; set; }

    /// <summary>Drops paths whose local write was skipped from the report.</summary>
    internal void Forget(HashSet<string> paths)
    {
        if (paths.Count == 0) return;
        Downloaded.RemoveAll(paths.Contains);
        Merged.RemoveAll(paths.Contains);
        DeletedHere.RemoveAll(paths.Contains);
        Conflicts.RemoveAll(c => paths.Contains(c.Path));
        Stale.RemoveAll(paths.Contains);
    }

    internal SyncReport ToReport()
    {
        bool moved = Uploaded.Count + Downloaded.Count + Merged.Count + DeletedHere.Count + DeletedInStorage.Count + Conflicts.Count + BackedUp.Count > 0;
        return new SyncReport(moved ? SyncOutcome.Synced : SyncOutcome.NothingToDo, null, Uploaded, Downloaded, Merged, DeletedHere, DeletedInStorage, Conflicts, BackedUp, Stale, Trashed, trashFolder: TrashFolder, backupFolder: BackupFolder, replaced: Replaced, skippedLinks: SkippedLinks, tooLarge: [.. TooLarge.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)], settling: [.. Settling.Order(StringComparer.Ordinal)]) { Notes = Notes, WaitingFolders = WaitingFolders, IncompleteFolders = IncompleteFolders };
    }
}

/// <summary>
/// Step 3 of the loop: for every path, L (local hash), B (hash at the last sync) and R (hash in the manifest; none for a tombstone or a missing entry) say who changed it.
/// Nothing is written here; downloads of the objects a merge or a download needs are the only I/O.
/// </summary>
internal static class SyncPlanner
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly UTF8Encoding Utf8 = new(false);

    internal static async Task<SyncPlan> Build(SyncOptions options, DateTime now, SyncStateStore store, Dictionary<string, LocalFile> realLocal,
        Dictionary<string, ManifestEntry> realBase, Dictionary<string, ManifestEntry> realRemote, bool firstConnection, ObjectFetcher fetcher,
        Dictionary<string, HashSet<string>> seen, IReadOnlySet<string> refused, CancellationToken ct,
        IReadOnlyCollection<string>? links = null, IReadOnlyCollection<string>? tooLarge = null)
    {
        SyncPlan plan = new() { SkippedLinks = [.. links ?? []] };
        plan.TooLarge.AddRange(tooLarge ?? []);
        bool whole = options.WholeFilesOnly;
        HashSet<string> big = new(tooLarge ?? [], StringComparer.Ordinal);
        string device = options.DeviceId;
        // Only when the host says which paths the app wrote, and never on a first connection (no base to tell what changed): the rest changed outside the app.
        IReadOnlySet<string>? editedInApp = firstConnection ? null : options.EditedInApp;

        // Whole change folders first: one that is gone from a side was archived, merged or renamed (it is followed there); the files below are then read as views in which that has happened.
        // A folder of binary or opaque files (attachments) has no such folders.
        FolderWork work = await FolderWork.Create(plan, store, fetcher, device, now, whole, ct, realLocal, realBase, realRemote, links);
        plan.WaitingFolders.AddRange(work.Waiting);
        plan.IncompleteFolders.AddRange(work.Incomplete);
        Dictionary<string, LocalFile> local = work.Local;
        Dictionary<string, ManifestEntry> baseEntries = work.Base, remote = work.Remote;

        // A file in the state folder, hash-checked while it was streamed in (or a base copy that had the hash); the bytes are read only where a merge or a report needs them.
        Task<FetchedObject> Fetch(string path, string hash) => fetcher.Get(work.SourceOf(path), hash, ct);

        // Whether a file missing on one side was removed on purpose. Change folders: their moves and event logs say (FolderWork). Whole files (attachments): the host says, from the items' logs; no host answer = plain deletions.
        async Task<bool> Explained(string path, FolderSide lacking) => whole ? options.RemovedOnPurpose?.Invoke(path) ?? true : await work.RemovalExplained(path, lacking);

        async Task PutBack(string path, FolderSide lacking)
        {
            if (!whole) { await work.PutBack(path, lacking); return; }
            if (AttachmentItem(path) is int item) plan.Notes.Add(new SyncNote(item, "", "put-back-file", $"put back attachment {path[(path.LastIndexOf('/') + 1)..]}: it was missing on {(lacking == FolderSide.Local ? "this computer" : "the storage")} and nothing says it was removed on purpose"));
        }

        ManifestEntry Live(ManifestEntry? current, string hash, string candidate)
        {
            List<string> history = current is null || current.Hash == hash
                ? [.. current?.History ?? []]
                : [.. new[] { current.Hash }.Concat(current.History).Distinct().Take(SyncModels.HistoryLimit)];
            return new ManifestEntry { Hash = hash, Clock = SyncClock.After(candidate, current?.Clock, device), Deleted = false, History = history };
        }

        ManifestEntry Tombstone(ManifestEntry current, string candidate) =>
            new() { Hash = current.Hash, Clock = SyncClock.After(candidate, current.Clock, device), Deleted = true, History = [.. current.History] };

        List<string> paths = [.. local.Keys.Concat(baseEntries.Keys).Concat(remote.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        // The objects the loop below is going to ask for are known from the three hashes alone: they are fetched six at a time now, and the loop finds them in the fetcher.
        // A path the loop might not fetch after all costs one download; one it fetches without being listed here is fetched then, one by one.
        List<(string Path, string Hash)> wanted = [];
        foreach (string path in paths)
        {
            if (links is { Count: > 0 } && links.Any(l => path == l || path.StartsWith(l + "/", StringComparison.Ordinal))) continue;
            if (big.Contains(path) || LocalTree.IsIgnored(path) || work.IsHeld(path)) continue;
            local.TryGetValue(path, out LocalFile? lf);
            baseEntries.TryGetValue(path, out ManifestEntry? be);
            remote.TryGetValue(path, out ManifestEntry? re);
            string? l = lf?.Hash, b = be is { Deleted: false } ? be.Hash : null, r = re is { Deleted: false } ? re.Hash : null;
            if (r is null || l == r || r == b) continue;                       // nothing to read from the storage, in sync, or only this computer changed it
            if (lf is { Settling: true } && l != b) continue;                  // left for the next pass
            if (l is null && b is not null && whole && options.RemovedOnPurpose is null) continue;                 // deleted here, edited in the storage, whole files: the loop does not read the dropped version
            wanted.Add((work.SourceOf(path), r));
        }
        await fetcher.Prefetch(wanted, ct);

        foreach (string path in paths)
        {
            try { await Visit(path); }
            catch (BoardStorageException e) when (e.IsTooLarge)
            {
                // The storage's copy is more than one stored file may be: this path is left alone in both directions, the rest of the sync goes on.
                plan.TooLarge.Add(path);
                plan.LinkedPaths.Add(path);
            }
        }

        async Task Visit(string path)
        {
            if (links is { Count: > 0 } && links.Any(l => path == l || path.StartsWith(l + "/", StringComparison.Ordinal)))
            {
                plan.LinkedPaths.Add(path);       // a link is in the way: nothing is read from it, written through it or deleted behind it, in either direction
                return;
            }
            if (big.Contains(path))
            {
                plan.LinkedPaths.Add(path);       // too large to upload from here: not uploaded, and not a deletion either
                return;
            }
            local.TryGetValue(path, out LocalFile? lf);
            baseEntries.TryGetValue(path, out ManifestEntry? be);
            remote.TryGetValue(path, out ManifestEntry? re);
            string? l = lf?.Hash, b = be is { Deleted: false } ? be.Hash : null, r = re is { Deleted: false } ? re.Hash : null;
            if (LocalTree.IsIgnored(path)) { plan.LinkedPaths.Add(path); return; }        // never synced, in either direction (a temp or editor file); not a deletion either
            if (work.IsHeld(path))
            {
                plan.LinkedPaths.Add(path);       // its change folder is held whole (a file in it still settling, or the folder incomplete in the storage): nothing is uploaded, downloaded or deleted
                if (lf is { Settling: true } && l != r && l != b) plan.Settling.Add(path);
                return;
            }
            if (lf is { Settling: true } && l != r && l != b)
            {
                plan.LinkedPaths.Add(path);       // written a moment ago and different from what was synced: still being written, so left for the next pass, exactly like a link
                plan.Settling.Add(path);
                return;
            }
            PlanItem item = new(path) { ExpectedLocalHash = l };
            bool outside = editedInApp is not null && !editedInApp.Contains(path) && !work.Virtual.Contains(path);
            bool probe = outside && re is not null && options.Storage.ConditionalWrites;

            if (l == r)                                                   // in sync (or gone on both sides)
            {
                if (lf is not null && !whole) plan.FinalContent[path] = () => lf.Bytes;
                return;
            }

            if (outside && !whole && IsEventLog(path) && lf is not null && r is not null && l != b)
            {
                // An event log is never replaced by an outside change, however old: it is merged with the storage's log (a union of lines; no base, or "only this side changed" would let the old log win whole), so no line can go.
                byte[] remoteLog = (await Fetch(path, r)).Bytes();
                string logStamp = SyncClock.Stamp(lf.LastWriteUtc, now, device);
                Merge(plan, item, path, lf, re!, null, remoteLog, logStamp, SyncClock.IsLater(re!.Clock, logStamp), store, Live, whole: false);
                if (item.Op == LocalOp.None && item.Upload is not null) { plan.Merged.Remove(path); plan.Uploaded.Add(path); }     // this computer's log had every line: a plain upload
                work.Add(item);
                return;
            }

            // A path the storage lost, or a file that a checkout or a hand left out: gone with nothing to explain it, it is put back (OpenSpec never deletes; a change is only archived, merged or renamed).
            // A removal is a removal only when its folder moved or the event log of the side that lacks it says why (see FolderWork.RemovalExplained). Whole-file folders (attachments) ask the host (SyncOptions.RemovedOnPurpose); without an answer they keep plain deletions.
            if (outside && l != b && re is not null && IsStale(path, l, b, r, re, seen, refused) && !(r is null && !whole && lf is not null && b is not null && !await work.RemovalExplained(path, FolderSide.Remote)))      // a removal the last sync already took in needs no new explanation
            {
                // Changed outside the app (git) to something the storage has already moved past: the storage wins, nothing is uploaded.
                if (r is null) { item.Op = LocalOp.Delete; plan.DeletedHere.Add(path); }
                else Download(plan, item, path, await Fetch(path, r));
                plan.Stale.Add(path);
                work.Add(item);
                return;
            }

            if (r == b)                                                   // only this computer changed it
            {
                if (lf is null)
                {
                    if (!await Explained(path, FolderSide.Local))
                    {
                        Download(plan, item, path, await Fetch(path, r!));        // not removed on purpose: it comes back
                        await PutBack(path, FolderSide.Local);
                    }
                    else
                    {
                        item.NewEntry = Tombstone(re!, SyncClock.Stamp(now, device));
                        plan.DeletedInStorage.Add(path);
                    }
                }
                else Upload(plan, item, lf, Live(re, lf.Hash, SyncClock.Stamp(lf.LastWriteUtc, now, device)), path, probe);
            }
            else if (l == b)                                              // only the storage changed it
            {
                if (r is null)
                {
                    if (!await Explained(path, FolderSide.Remote))
                    {
                        Upload(plan, item, lf!, Live(re, lf!.Hash, SyncClock.Stamp(lf.LastWriteUtc, now, device)), path, probe);      // a marker nothing explains is ignored: the file is uploaded again
                        await PutBack(path, FolderSide.Remote);
                    }
                    else { item.Op = LocalOp.Delete; plan.DeletedHere.Add(path); }
                }
                else Download(plan, item, path, await Fetch(path, r));
            }
            else if (l is not null && r is not null)                      // both changed it, both have content
            {
                FetchedObject remoteObject = await Fetch(path, r);
                string stamp = SyncClock.Stamp(lf!.LastWriteUtc, now, device);
                bool remoteIsNewer = SyncClock.IsLater(re!.Clock, stamp);
                string? committed = whole ? null : options.CommittedContent?.Invoke(path);      // the last commit's version: the base when this computer has none saved
                if (firstConnection && !(committed is not null && TryText(lf.Bytes) is not null && TryText(remoteObject.Bytes()) is not null))
                {
                    // No base exists yet, so no finer merge: the storage's content wins and this computer's copy is kept aside.
                    item.BackupOf = lf.Bytes;
                    Download(plan, item, path, remoteObject);
                }
                else Merge(plan, item, path, lf, re, b, remoteObject.Bytes(), stamp, remoteIsNewer, store, Live, whole, committed);
            }
            else if (l is null)                                           // deleted here, edited in the storage
            {
                string stamp = SyncClock.Stamp(now, device);
                if (!await Explained(path, FolderSide.Local))
                {
                    Download(plan, item, path, await Fetch(path, r!));        // not removed on purpose: it comes back, with the edit
                    await PutBack(path, FolderSide.Local);
                }
                else if (SyncClock.IsLater(re!.Clock, stamp)) Download(plan, item, path, await Fetch(path, r!));    // the edit is later: it brings the file back
                else
                {
                    string dropped = whole ? "the version edited on another computer" : Lenient((await Fetch(path, r!)).Bytes());     // fetched before anything is changed: an object that is too large skips the whole path
                    item.NewEntry = Tombstone(re, stamp);
                    plan.DeletedInStorage.Add(path);
                    plan.Conflicts.Add(new SyncConflict(path, "deleted on this computer, edited on another", "deleted", dropped, DroppedHere: false));
                }
            }
            else                                                          // edited here, deleted in the storage (r is null, so re is its tombstone, or the storage lost the entry)
            {
                string stamp = SyncClock.Stamp(lf!.LastWriteUtc, now, device);
                if (!await Explained(path, FolderSide.Remote))
                {
                    Upload(plan, item, lf, Live(re, lf.Hash, stamp), path, probe);      // not removed on purpose: the edit brings the file back
                    await PutBack(path, FolderSide.Remote);
                }
                else
                {
                    if (re is null) throw new SyncFailure($"The manifest in the storage has no entry for \"{path}\", which this computer synced before and has edited since, so nothing was synced. Another computer may have damaged the storage; sync from the computer that has the file.");
                    if (SyncClock.IsLater(stamp, re.Clock)) Upload(plan, item, lf, Live(re, lf.Hash, stamp), path, probe);    // the edit is later: the file comes back
                    else
                    {
                        item.Op = LocalOp.Delete;
                        plan.DeletedHere.Add(path);
                        plan.Conflicts.Add(new SyncConflict(path, "edited on this computer, deleted on another", "deleted", whole ? "the version edited on this computer (kept in the trash)" : Lenient(lf.Bytes), DroppedHere: true));
                    }
                }
            }
            work.Add(item);
        }

        await work.Finish();       // followed files are written where they went; edits made here are carried into merged items; notes name the other destination
        return plan;
    }

    /// <summary>
    /// A local change outside the app is stale when it puts back content this path has had before: an earlier version in the manifest history, any content this computer
    /// ever saw for the path (<paramref name="seen"/>, no ceiling), content the storage refused to take again (<paramref name="refused"/>: its object already exists),
    /// or, for a tombstone, the last content; or when the file is gone here although the storage changed it since the last sync (a checkout removed it, nobody deleted it on purpose).
    /// A deletion of a file nobody else changed (base = storage) is real and follows the normal rules.
    /// ponytail: the manifest history keeps 20 versions, but it is no longer the limit: older versions are found in this computer's memory or by the storage refusing the upload.
    /// </summary>
    private static bool IsStale(string path, string? l, string? b, string? r, ManifestEntry re, Dictionary<string, HashSet<string>> seen, IReadOnlySet<string> refused) =>
        l is null
            ? r is not null && b != r
            : re.History.Contains(l) || (re.Deleted && re.Hash == l) || (seen.TryGetValue(path, out HashSet<string>? known) && known.Contains(l)) || refused.Contains(path);

    /// <summary>The work item an attachment path belongs to (wi&lt;id&gt;/…); null for any other path.</summary>
    private static int? AttachmentItem(string path)
    {
        int slash = path.IndexOf('/');
        return slash > 2 && path.StartsWith("wi", StringComparison.Ordinal) && int.TryParse(path.AsSpan(2, slash - 2), out int id) ? id : null;
    }

    private static bool IsEventLog(string path) => path == "events.log" || path.EndsWith("/events.log", StringComparison.Ordinal);

    private static string Lenient(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    private static void Upload(SyncPlan plan, PlanItem item, LocalFile lf, ManifestEntry entry, string path, bool probe = false)
    {
        item.Probe = probe;
        item.NewEntry = entry;
        item.Upload = () => lf.Bytes;
        item.ExpectedLocalHash = lf.Hash;
        plan.FinalContent[path] = () => lf.Bytes;
        plan.Uploaded.Add(path);
    }

    private static void Download(SyncPlan plan, PlanItem item, string path, FetchedObject fetched)
    {
        item.Op = LocalOp.Write;
        item.WriteFrom = fetched;
        item.KeepOld = true;
        plan.FinalFiles[path] = fetched.FilePath;
        plan.Downloaded.Add(path);
    }

    private static void Download(SyncPlan plan, PlanItem item, string path, byte[] bytes)
    {
        item.Op = LocalOp.Write;
        item.WriteBytes = bytes;
        item.KeepOld = true;
        plan.FinalContent[path] = () => bytes;
        plan.Downloaded.Add(path);
    }

    private static void Merge(SyncPlan plan, PlanItem item, string path, LocalFile lf, ManifestEntry re, string? baseHash, byte[] remoteBytes,
        string stamp, bool remoteIsNewer, SyncStateStore store, Func<ManifestEntry?, string, string, ManifestEntry> live, bool whole, string? committedBase = null)
    {
        // Whole files only: never a text merge, whatever the name or the content; the bytes are not even decoded.
        string? localText = whole ? null : TryText(lf.Bytes), remoteText = whole ? null : TryText(remoteBytes);
        if (localText is null || remoteText is null)
        {
            // Not text on one side (or whole files only): the newer side wins whole.
            string kept = remoteIsNewer ? "the version from the storage" : "the version on this computer";
            string dropped = remoteIsNewer ? "the version on this computer" : "the version from the storage";
            plan.Conflicts.Add(new SyncConflict(path, whole ? "changed on both computers, so the newer whole file was kept" : "the file is not text, so it cannot be merged", kept, dropped, DroppedHere: remoteIsNewer));
            if (remoteIsNewer)
            {
                item.TrashOf = lf.Bytes;       // the only case where a write destroys bytes that are nowhere else: they go to the trash first
                Download(plan, item, path, remoteBytes);
            }
            else Upload(plan, item, lf, live(re, lf.Hash, stamp), path);      // the local version is uploaded, so the dropped storage version stays in the storage's objects
            return;
        }

        byte[]? baseBytes = baseHash is null ? null : store.ReadBase(baseHash);
        string? baseText = baseBytes is null ? committedBase : TryText(baseBytes);      // no saved base: the committed version is the common ancestor
        MergeResult merged = BoardSyncApi.Merge(path, baseText, localText, remoteText, remoteIsNewer);
        byte[] mergedBytes = Utf8.GetBytes(merged.Text);
        string mergedHash = SyncModels.HashOf(mergedBytes);
        plan.Merged.Add(path);
        plan.FinalContent[path] = () => mergedBytes;
        foreach (MergeConflict c in merged.Conflicts) plan.Conflicts.Add(new SyncConflict(path, c.What, c.Kept, c.Dropped, DroppedHere: remoteIsNewer));   // every merge rule keeps the newer side

        if (mergedHash != re.Hash)
        {
            item.NewEntry = live(re, mergedHash, stamp);
            item.Upload = () => mergedBytes;
        }
        if (mergedHash != lf.Hash)
        {
            item.Op = LocalOp.Write;
            item.WriteBytes = mergedBytes;
        }
    }

    private static string? TryText(byte[] bytes)
    {
        try { return StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { return null; }
    }
}
