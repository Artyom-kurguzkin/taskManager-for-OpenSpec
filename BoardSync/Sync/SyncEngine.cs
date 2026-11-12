using System.Collections.Concurrent;
using BoardSync.PublicDTOs;

namespace BoardSync.Sync;

/// <summary>
/// One sync: scan, read the manifest, decide per path (see <see cref="SyncPlanner"/>), upload objects, replace the manifest only if nobody else did,
/// and only then touch the local files and save the state.
/// </summary>
internal static class SyncEngine
{
    private const int MaxRounds = 5;

    /// <summary>How far back the deletion brake counts (see <see cref="SyncPlanner"/>).</summary>
    private static readonly TimeSpan DeletionWindowLength = TimeSpan.FromHours(24);

    // ponytail: one lock per directory for the life of the process; entries are never removed (a handful of projects).
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);

    internal static async Task<SyncReport> Run(SyncOptions options, SyncHooks? hooks, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        string root = Path.GetFullPath(options.LocalRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        SemaphoreSlim gate = Gates.GetOrAdd(root, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            return await Once(options, root, hooks, ct);
        }
        catch (BoardStorageException e) { return SyncReport.Of(SyncOutcome.Failed, e.Message); }
        catch (SyncFailure e) { return SyncReport.Of(SyncOutcome.Failed, e.Message); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return SyncReport.Of(SyncOutcome.Failed, $"A file on this computer could not be written: {e.Message} Sync again; nothing is lost if the storage was already updated.");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Whatever else a hostile or damaged storage provokes: a plain failure, never an exception out of the API.
            return SyncReport.Of(SyncOutcome.Failed, $"The sync stopped because of something unexpected ({e.GetType().Name}: {e.Message}). Nothing was changed on purpose; sync again, and check the storage if it repeats.");
        }
        finally { gate.Release(); }
    }

    private static async Task<SyncReport> Once(SyncOptions options, string root, SyncHooks? hooks, CancellationToken ct)
    {
        SyncStateStore store = new(options.StateFolder);
        SyncStateFile? state = store.Load();
        string? pause = GitGuard.Pause(root, options.GitFolder, state?.Entries, checkMarkers: !options.WholeFilesOnly);
        if (pause is not null) return SyncReport.Of(SyncOutcome.Blocked, pause, SyncBlock.GitBusy);       // before anything is read or written, here or in the storage
        DateTime now = (options.UtcNow ?? (() => DateTime.UtcNow))();
        if (state is not null && !Directory.Exists(root))
            return SyncReport.Of(SyncOutcome.Failed, $"The board folder \"{root}\" does not exist, so nothing was synced. Nothing was deleted anywhere.");
        List<string> links = [], tooLarge = [];
        Dictionary<string, LocalFile> local = LocalTree.Scan(root, links, tooLarge, hooks?.OnScanBuffer);          // links are not followed: what is behind one is not this board's; a file over the cap is not read at all
        List<SyncStateStore.DeletionEntry> deletions = store.LoadDeletions(now, DeletionWindowLength);
        DeletionWindow window = new(deletions.Sum(d => d.Here), deletions.Sum(d => d.Storage), deletions.Count > 0 ? deletions[0].Live : null);
        Dictionary<string, byte[]> fetched = new(StringComparer.Ordinal);     // objects already downloaded in an earlier round
        Dictionary<string, HashSet<string>> seen = store.LoadSeen();           // every content this computer ever saw for a path
        HashSet<string> inherited = store.LoadPending();                       // objects a sync that died was about to create
        HashSet<string> pending = new(inherited, StringComparer.Ordinal);      // what pending.json holds now
        HashSet<string> created = new(StringComparer.Ordinal);                 // objects this sync put, in any round
        bool track = options.EditedInApp is not null;
        bool seenChanged = false;

        for (int round = 1; round <= MaxRounds; round++)
        {
            (ManifestFile? manifest, string? version) = await ReadManifest(options.Storage, round == 1 ? state?.StorageVersion : null, state, ct);
            Dictionary<string, ManifestEntry> remote = manifest?.Entries ?? [];
            foreach (ManifestEntry entry in remote.Values) if (SyncClock.IsValid(entry.Clock)) entry.Clock = SyncClock.Clamp(entry.Clock, now);     // a clock far ahead of this computer's counts as now
            // A storage that lost its manifest while this computer remembers one is not a mass delete: everything here is uploaded again.
            Dictionary<string, ManifestEntry> baseEntries = state is not null && manifest is not null ? state.Entries : [];
            seenChanged |= Remember(seen, remote);

            HashSet<string> refused = new(StringComparer.Ordinal);
            SyncPlan plan = await SyncPlanner.Build(options, now, store, local, baseEntries, remote, firstConnection: state is null, fetched, seen, refused, ct, links, window, tooLarge);
            if (plan.BlockedMessage is not null) return SyncReport.Of(SyncOutcome.Blocked, plan.BlockedMessage, SyncBlock.MassDelete);

            List<PlanItem> uploads = plan.Items.Where(i => i.Upload is not null && i.NewEntry is not null).ToList();
            if (uploads.Count > 0)
            {
                // Written down before the first put: if this sync dies after an object went up, the next one must not take that object for proof that the content is old.
                // Only a host that passes EditedInApp ever takes a refusal for a sign of old content; any other keeps no file.
                if (track)
                {
                    foreach (PlanItem item in uploads) pending.Add(SyncModels.ObjectPath(item.Path, item.NewEntry!.Hash));
                    store.SavePending(pending);
                }
                foreach (PlanItem item in uploads.Where(i => i.Probe))
                {
                    string key = SyncModels.ObjectPath(item.Path, item.NewEntry!.Hash);
                    if (created.Contains(key)) continue;
                    PutResult probe = await options.Storage.Put(key, ReadForUpload(item), PutCondition.OnlyIfAbsent, ct);
                    if (probe.Written || inherited.Contains(key)) { created.Add(key); continue; }
                    refused.Add(item.Path);                // already there, and not by us: this path has had this content before
                    pending.Remove(key);
                    if (track) store.SavePending(pending);
                }
                if (refused.Count > 0)
                {
                    plan = await SyncPlanner.Build(options, now, store, local, baseEntries, remote, firstConnection: state is null, fetched, seen, refused, ct, links, window, tooLarge);
                    if (plan.BlockedMessage is not null) return SyncReport.Of(SyncOutcome.Blocked, plan.BlockedMessage, SyncBlock.MassDelete);
                    uploads = plan.Items.Where(i => i.Upload is not null && i.NewEntry is not null).ToList();
                }
            }

            Dictionary<string, ManifestEntry> entries = SyncModels.CopyOf(remote);
            foreach (PlanItem item in plan.Items.Where(i => i.NewEntry is not null)) entries[item.Path] = item.NewEntry!;
            string? newVersion = version;

            if (plan.Items.Any(i => i.NewEntry is not null))
            {
                foreach (PlanItem item in uploads)
                {
                    string key = SyncModels.ObjectPath(item.Path, item.NewEntry!.Hash);
                    if (created.Add(key)) await options.Storage.Put(key, ReadForUpload(item), PutCondition.OnlyIfAbsent, ct);   // refused = already there
                }
                // ponytail: one manifest for the whole board, so every change rewrites it; split per work item when many people sync.
                byte[] bytes = SyncModels.Serialize(new ManifestFile { Entries = entries });
                PutCondition condition = manifest is null ? PutCondition.OnlyIfAbsent : PutCondition.OnlyIfVersion(version!);
                PutResult put = await options.Storage.Put(SyncModels.ManifestPath, bytes, condition, ct);
                if (!put.Written) continue;                    // another computer replaced the manifest first: start again from theirs
                newVersion = put.Version;
            }

            if (hooks?.SkipLocalApply == true) return plan.ToReport();

            HashSet<string> skipped = Apply(plan, root, now, store);
            skipped.UnionWith(plan.LinkedPaths);             // left alone because of a link: the saved state must not claim they are in sync
            foreach (string path in skipped)
            {
                // The file changed while this sync ran: leave it, and keep the old base so the next sync merges instead of overwriting.
                if (baseEntries.TryGetValue(path, out ManifestEntry? old)) entries[path] = old.Copy(); else entries.Remove(path);
            }

            // After a skipped path the saved entries are no longer the manifest of that version, so the next sync must read the manifest again instead of trusting the version.
            SyncStateFile next = new() { StorageVersion = skipped.Count > 0 ? state?.StorageVersion : newVersion, Entries = entries };
            plan.Forget(skipped);
            if (state is null || !SyncModels.Serialize(state).AsSpan().SequenceEqual(SyncModels.Serialize(next)))
                store.Save(next, options.WholeFilesOnly ? new Dictionary<string, Func<byte[]>>() : plan.FinalContent);        // whole files are never merged, so they need no base copies
            seenChanged |= Remember(seen, entries);
            if (seenChanged) store.SaveSeen(seen);
            if (options.AllowMassDelete) { if (deletions.Count > 0) store.SaveDeletions([]); }            // an allowed mass deletion starts the window again
            else if (plan.DeletesHere + plan.DeletesStorage > 0)
            {
                deletions.Add(new SyncStateStore.DeletionEntry(now, plan.DeletesHere, plan.DeletesStorage, plan.BaseLive));
                store.SaveDeletions(deletions);
            }
            store.ClearPending();                            // everything this sync put is in the manifest now
            store.PruneTrash(now);                           // only after a sync that went through
            return plan.ToReport();
        }
        return SyncReport.Of(SyncOutcome.Failed, $"Other computers kept changing the storage at the same time, so this sync gave up after {MaxRounds} tries. Nothing was changed here. Sync again in a moment.");
    }

    /// <summary>The bytes of an upload, read now (one file at a time) and checked against the hash the manifest entry is going to claim for them.</summary>
    private static byte[] ReadForUpload(PlanItem item)
    {
        byte[] bytes = item.Upload!();
        if (SyncModels.HashOf(bytes) != item.NewEntry!.Hash) throw new SyncFailure($"\"{item.Path}\" changed while it was being synced, so nothing was synced. Sync again in a moment.");
        return bytes;
    }

    /// <summary>Adds every content hash of the entries (current and history) to the per-path memory; true when something was new.</summary>
    private static bool Remember(Dictionary<string, HashSet<string>> seen, Dictionary<string, ManifestEntry> entries)
    {
        bool added = false;
        foreach ((string path, ManifestEntry entry) in entries)
        {
            if (!seen.TryGetValue(path, out HashSet<string>? known)) seen[path] = known = new HashSet<string>(StringComparer.Ordinal);
            added |= known.Add(entry.Hash);
            foreach (string hash in entry.History) added |= known.Add(hash);
        }
        return added;
    }

    private static async Task<(ManifestFile? Manifest, string? Version)> ReadManifest(IBoardStorage storage, string? knownVersion, SyncStateFile? state, CancellationToken ct)
    {
        StoredFile? file = await storage.GetIfChanged(SyncModels.ManifestPath, knownVersion, ct);
        if (file is null) return (null, null);
        if (file.IsNotModified) return (new ManifestFile { Entries = state!.Entries }, state.StorageVersion);
        return (SyncModels.ParseManifest(file.Bytes), file.Version);
    }

    /// <summary>Writes the local files the plan asks for and removes the ones it deletes by moving them to the trash. Returns the paths it left alone because the file no longer was what the scan saw.</summary>
    private static HashSet<string> Apply(SyncPlan plan, string root, DateTime now, SyncStateStore store)
    {
        HashSet<string> skipped = new(StringComparer.Ordinal);
        foreach (PlanItem item in plan.Items.Where(i => i.Op != LocalOp.None))
        {
            string file = LocalTree.ToLocal(root, item.Path);
            if (LocalTree.ThroughLink(root, file)) { skipped.Add(item.Path); continue; }       // a link appeared between the scan and now, or the path leaves the folder
            string? currentHash = LocalTree.HashOfFile(file);           // streamed: a large file is not read into memory to be compared
            if (currentHash != item.ExpectedLocalHash) { skipped.Add(item.Path); continue; }
            string? trashed = null, replaced = null, backedUp = null;
            try
            {
                // The sync never erases a file on this computer: what it removes is moved, what it overwrites for good is copied, to the trash first.
                if (item.Op == LocalOp.Delete) trashed = store.MoveToTrash(now, item.Path, file);
                else if (item.TrashOf is not null) trashed = store.CopyToTrash(now, item.Path, item.TrashOf);
                else if (item.KeepOld && item.BackupOf is null && currentHash is not null && currentHash != SyncModels.HashOf(item.WriteBytes!))
                {
                    byte[]? current = LocalTree.ReadOrNull(file);          // only now, and only this one
                    if (current is null || SyncModels.HashOf(current) != currentHash) { skipped.Add(item.Path); continue; }      // it changed meanwhile: left as it is
                    replaced = store.CopyToTrash(now, item.Path, current);
                }
                if (item.BackupOf is not null) backedUp = store.WriteBackup(now, item.Path, item.BackupOf);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                skipped.Add(item.Path);                      // the file stays as it is; the next sync tries again
                continue;
            }
            if (trashed is not null) { plan.Trashed.Add(trashed); plan.TrashFolder = store.TrashFolder(now); }
            if (replaced is not null) { plan.Replaced.Add(replaced); plan.TrashFolder = store.TrashFolder(now); }
            if (backedUp is not null) { plan.BackedUp.Add(backedUp); plan.BackupFolder = store.BackupFolder(now); }
            if (item.Op == LocalOp.Write) LocalTree.WriteAtomic(file, item.WriteBytes!);
            else LocalTree.RemoveEmptyParents(root, file);
        }
        return skipped;
    }
}
