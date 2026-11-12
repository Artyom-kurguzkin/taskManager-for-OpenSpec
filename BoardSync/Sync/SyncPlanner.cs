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

/// <summary>The deletions this computer applied or tombstoned in the last 24 hours, and how many live paths the board had when that window began.</summary>
internal sealed record DeletionWindow(int Here, int Storage, int? Live);

internal sealed class SyncPlan
{
    internal List<PlanItem> Items { get; } = [];
    internal string? BlockedMessage { get; set; }
    /// <summary>The content every live path ends with, read only when a base copy has to be written.</summary>
    internal Dictionary<string, Func<byte[]>> FinalContent { get; } = new(StringComparer.Ordinal);
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
    internal List<string> Trashed { get; } = [];
    /// <summary>Copies of local files that a download replaced (in the trash), not files the sync removed.</summary>
    internal List<string> Replaced { get; } = [];
    /// <summary>Paths the plan left alone because a link on this computer is in the way (the link itself, or something the storage has under it).</summary>
    internal HashSet<string> LinkedPaths { get; } = new(StringComparer.Ordinal);
    internal List<string> SkippedLinks { get; set; } = [];
    internal int DeletesHere { get; set; }
    internal int DeletesStorage { get; set; }
    internal int BaseLive { get; set; }
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
        return new SyncReport(moved ? SyncOutcome.Synced : SyncOutcome.NothingToDo, null, Uploaded, Downloaded, Merged, DeletedHere, DeletedInStorage, Conflicts, BackedUp, Stale, Trashed, trashFolder: TrashFolder, backupFolder: BackupFolder, replaced: Replaced, skippedLinks: SkippedLinks, tooLarge: [.. TooLarge.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
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

    internal static async Task<SyncPlan> Build(SyncOptions options, DateTime now, SyncStateStore store, Dictionary<string, LocalFile> local,
        Dictionary<string, ManifestEntry> baseEntries, Dictionary<string, ManifestEntry> remote, bool firstConnection, Dictionary<string, byte[]> fetched,
        Dictionary<string, HashSet<string>> seen, IReadOnlySet<string> refused, CancellationToken ct,
        IReadOnlyCollection<string>? links = null, DeletionWindow? window = null, IReadOnlyCollection<string>? tooLarge = null)
    {
        SyncPlan plan = new() { SkippedLinks = [.. links ?? []] };
        plan.TooLarge.AddRange(tooLarge ?? []);
        bool whole = options.WholeFilesOnly;
        HashSet<string> big = new(tooLarge ?? [], StringComparer.Ordinal);
        string device = options.DeviceId;
        int deletesHere = 0, deletesStorage = 0;
        // Only when the host says which paths the app wrote, and never on a first connection (no base to tell what changed): the rest changed outside the app.
        IReadOnlySet<string>? editedInApp = firstConnection ? null : options.EditedInApp;

        async Task<byte[]> Fetch(string path, string hash)
        {
            if (fetched.TryGetValue(hash, out byte[]? known)) return known;
            StoredFile? file = await options.Storage.Get(SyncModels.ObjectPath(path, hash), ct)
                ?? throw new SyncFailure($"The storage has no stored copy of \"{path}\" (content {hash[..8]}), so nothing was synced. Another computer may have cleaned up the storage; sync again from the computer that has the file.");
            if (SyncModels.HashOf(file.Bytes) != hash) throw new SyncFailure($"The stored copy of \"{path}\" in the storage is damaged, so nothing was synced.");
            return fetched[hash] = file.Bytes;
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

        foreach (string path in local.Keys.Concat(baseEntries.Keys).Concat(remote.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
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
            PlanItem item = new(path) { ExpectedLocalHash = l };
            bool outside = editedInApp is not null && !editedInApp.Contains(path);
            bool probe = outside && re is not null && options.Storage.ConditionalWrites;

            if (l == r)                                                   // in sync (or gone on both sides)
            {
                if (lf is not null && !whole) plan.FinalContent[path] = () => lf.Bytes;
                return;
            }

            if (outside && !whole && IsEventLog(path) && lf is not null && r is not null && l != b)
            {
                // An event log is never replaced by an outside change, however old: it is merged with the storage's log (a union of lines; no base, or "only this side changed" would let the old log win whole), so no line can go.
                byte[] remoteLog = await Fetch(path, r);
                string logStamp = SyncClock.Stamp(lf.LastWriteUtc, now, device);
                Merge(plan, item, path, lf, re!, null, remoteLog, logStamp, SyncClock.IsLater(re!.Clock, logStamp), store, Live, whole: false);
                if (item.Op == LocalOp.None && item.Upload is not null) { plan.Merged.Remove(path); plan.Uploaded.Add(path); }     // this computer's log had every line: a plain upload
                plan.Items.Add(item);
                return;
            }

            if (outside && l != b && re is not null && IsStale(path, l, b, r, re, seen, refused))
            {
                // Changed outside the app (git) to something the storage has already moved past: the storage wins, nothing is uploaded.
                if (r is null) { item.Op = LocalOp.Delete; plan.DeletedHere.Add(path); deletesHere++; }
                else Download(plan, item, path, await Fetch(path, r));
                plan.Stale.Add(path);
                plan.Items.Add(item);
                return;
            }

            if (r == b)                                                   // only this computer changed it
            {
                if (lf is null)
                {
                    item.NewEntry = Tombstone(re!, SyncClock.Stamp(now, device));
                    plan.DeletedInStorage.Add(path);
                    deletesStorage++;
                }
                else Upload(plan, item, lf, Live(re, lf.Hash, SyncClock.Stamp(lf.LastWriteUtc, now, device)), path, probe);
            }
            else if (l == b)                                              // only the storage changed it
            {
                if (r is null) { item.Op = LocalOp.Delete; plan.DeletedHere.Add(path); deletesHere++; }
                else Download(plan, item, path, await Fetch(path, r));
            }
            else if (l is not null && r is not null)                      // both changed it, both have content
            {
                byte[] remoteBytes = await Fetch(path, r);
                string stamp = SyncClock.Stamp(lf!.LastWriteUtc, now, device);
                bool remoteIsNewer = SyncClock.IsLater(re!.Clock, stamp);
                if (firstConnection)
                {
                    // No base exists yet, so no finer merge: the storage's content wins and this computer's copy is kept aside.
                    item.BackupOf = lf.Bytes;
                    Download(plan, item, path, remoteBytes);
                }
                else Merge(plan, item, path, lf, re, b, remoteBytes, stamp, remoteIsNewer, store, Live, whole);
            }
            else if (l is null)                                           // deleted here, edited in the storage
            {
                string stamp = SyncClock.Stamp(now, device);
                if (SyncClock.IsLater(re!.Clock, stamp)) Download(plan, item, path, await Fetch(path, r!));    // the edit is later: it brings the file back
                else
                {
                    string dropped = whole ? "the version edited on another computer" : Lenient(await Fetch(path, r!));     // fetched before anything is changed: an object that is too large skips the whole path
                    item.NewEntry = Tombstone(re, stamp);
                    plan.DeletedInStorage.Add(path);
                    deletesStorage++;
                    plan.Conflicts.Add(new SyncConflict(path, "deleted on this computer, edited on another", "deleted", dropped, DroppedHere: false));
                }
            }
            else                                                          // edited here, deleted in the storage (r is null, so re is its tombstone)
            {
                string stamp = SyncClock.Stamp(lf!.LastWriteUtc, now, device);
                if (SyncClock.IsLater(stamp, re!.Clock)) Upload(plan, item, lf, Live(re, lf.Hash, stamp), path, probe);    // the edit is later: the file comes back
                else
                {
                    item.Op = LocalOp.Delete;
                    plan.DeletedHere.Add(path);
                    deletesHere++;
                    plan.Conflicts.Add(new SyncConflict(path, "edited on this computer, deleted on another", "deleted", whole ? "the version edited on this computer (kept in the trash)" : Lenient(lf.Bytes), DroppedHere: true));
                }
            }
            plan.Items.Add(item);
        }


        int baseLive = baseEntries.Values.Count(e => !e.Deleted);
        plan.DeletesHere = deletesHere;
        plan.DeletesStorage = deletesStorage;
        plan.BaseLive = baseLive;
        // The brake counts the last 24 hours, not one sync: small deletions repeated sync after sync add up. The ceiling is a share of the board as it was when that window began.
        int windowLive = window?.Live ?? baseLive;
        int limit = Math.Max(10, (int)Math.Ceiling(windowLive * 0.3));
        int totalHere = deletesHere + (window?.Here ?? 0), totalStorage = deletesStorage + (window?.Storage ?? 0);
        if (!options.AllowMassDelete && (totalHere > limit || totalStorage > limit))
        {
            List<string> reasons = [];
            string recent = window is { Here: > 0 } or { Storage: > 0 } ? " in the last 24 hours" : "";
            if (totalHere > limit) reasons.Add($"This sync would delete {totalHere} of {windowLive} board files on this computer{recent}.");
            if (totalStorage > limit) reasons.Add($"This sync would delete {totalStorage} of {windowLive} board files in the storage{recent}.");
            plan.BlockedMessage = string.Join(" ", reasons) + " That is more than usual, often a wrong folder or a storage that was reset, so nothing was changed. If it is what you want, allow it and sync again.";
        }
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

    private static void Download(SyncPlan plan, PlanItem item, string path, byte[] bytes)
    {
        item.Op = LocalOp.Write;
        item.WriteBytes = bytes;
        item.KeepOld = true;
        plan.FinalContent[path] = () => bytes;
        plan.Downloaded.Add(path);
    }

    private static void Merge(SyncPlan plan, PlanItem item, string path, LocalFile lf, ManifestEntry re, string? baseHash, byte[] remoteBytes,
        string stamp, bool remoteIsNewer, SyncStateStore store, Func<ManifestEntry?, string, string, ManifestEntry> live, bool whole)
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
        string? baseText = baseBytes is null ? null : TryText(baseBytes);
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
