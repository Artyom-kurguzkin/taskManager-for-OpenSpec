namespace BoardSync.Sync;

/// <summary>This computer's private state for one project: state.json (the last synced manifest entries) and base/ (the content of each of them, the merge base).</summary>
internal sealed class SyncStateStore(string folder)
{
    private string StatePath => Path.Combine(folder, "state.json");
    private string BasePath(string hash) => Path.Combine(folder, "base", hash[..2], hash);

    // Every read below opens the file shared (read, write and delete): a writer in another process (or a status line reading while a pass saves) never collides with it, and every write
    // goes to a unique temp file next to the target and is renamed over it (LocalTree.WriteAtomic), so a reader sees the old file or the new one, never half of one.
    internal SyncStateFile? Load() => LocalTree.ReadOrNull(StatePath) is { } bytes ? SyncModels.ParseState(bytes) : null;

    internal byte[]? ReadBase(string hash) => LocalTree.ReadOrNull(BasePath(hash));

    /// <summary>The file holding the base copy with this content hash, or null when there is none.</summary>
    internal string? BaseFile(string hash)
    {
        string file = BasePath(hash);
        return File.Exists(file) ? file : null;
    }

    /// <summary>
    /// Writes the base copies the new state needs, then the state itself, then drops base copies nothing refers to. Only called after a sync succeeded.
    /// A path in <paramref name="fileByPath"/> has its content in a file already (a download): that file is copied, never read whole into memory.
    /// </summary>
    internal void Save(SyncStateFile state, IReadOnlyDictionary<string, Func<byte[]>> contentByPath, IReadOnlyDictionary<string, string>? fileByPath = null)
    {
        HashSet<string> wanted = [];
        foreach ((string path, ManifestEntry entry) in state.Entries)
        {
            if (entry.Deleted) continue;
            wanted.Add(entry.Hash);
            if (File.Exists(BasePath(entry.Hash)) || LocalTree.ThroughLink(folder, BasePath(entry.Hash))) continue;
            if (fileByPath is not null && fileByPath.TryGetValue(path, out string? source)) { FileCopy.CopyVerified(source, BasePath(entry.Hash), entry.Hash); continue; }     // a mismatch leaves no base copy: it is only a help for merging
            if (!contentByPath.TryGetValue(path, out Func<byte[]>? content)) continue;
            byte[] bytes = content();      // read only now, one file at a time
            if (SyncModels.HashOf(bytes) == entry.Hash) LocalTree.WriteAtomic(BasePath(entry.Hash), bytes);     // a base copy is only a help for merging: through a link it is left out
        }
        LocalTree.WriteAtomic(StatePath, SyncModels.Serialize(state));
        Prune(wanted);
    }

    private void Prune(HashSet<string> wanted)
    {
        string baseFolder = Path.Combine(folder, "base");
        if (!Directory.Exists(baseFolder)) return;
        foreach (string file in Directory.EnumerateFiles(baseFolder, "*", SearchOption.AllDirectories))
        {
            if (wanted.Contains(Path.GetFileName(file))) continue;
            try { File.Delete(file); } catch (IOException) { }
        }
        foreach (string dir in Directory.EnumerateDirectories(baseFolder))
        {
            if (!Directory.EnumerateFileSystemEntries(dir).Any()) try { Directory.Delete(dir); } catch (IOException) { }
        }
    }

    private string ScanPath => Path.Combine(folder, "scan.json");

    /// <summary>The stat cache the last walk left (see <see cref="StatCache"/>); a missing or damaged file is the same as none, and everything is read again.</summary>
    internal StatCacheFile? LoadScan()
    {
        try { return ReadJson<StatCacheFile>(ScanPath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    internal void SaveScan(StatCacheFile file) =>
        LocalTree.WriteAtomic(ScanPath, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(file));

    private string SeenPath => Path.Combine(folder, "seen.json");
    private string PendingPath => Path.Combine(folder, "pending.json");

    /// <summary>Every content hash this computer has ever seen for each path, in any manifest it read or by uploading it. No cap.</summary>
    internal Dictionary<string, HashSet<string>> LoadSeen()
    {
        Dictionary<string, HashSet<string>> seen = new(StringComparer.Ordinal);
        foreach ((string path, List<string> hashes) in ReadJson<Dictionary<string, List<string>>>(SeenPath) ?? [])
            seen[path] = new HashSet<string>(hashes ?? [], StringComparer.Ordinal);
        return seen;
    }

    internal void SaveSeen(Dictionary<string, HashSet<string>> seen) =>
        LocalTree.WriteAtomic(SeenPath, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new SortedDictionary<string, List<string>>(seen.ToDictionary(e => e.Key, e => e.Value.Order(StringComparer.Ordinal).ToList()), StringComparer.Ordinal)));

    /// <summary>The object keys a sync that did not finish was about to create; a refused upload of one of them is our own earlier put, not proof that the content is old.</summary>
    internal HashSet<string> LoadPending() => new(ReadJson<List<string>>(PendingPath) ?? [], StringComparer.Ordinal);

    internal void SavePending(IEnumerable<string> keys) =>
        LocalTree.WriteAtomic(PendingPath, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(keys.Order(StringComparer.Ordinal).ToList()));

    internal void ClearPending()
    {
        try { File.Delete(PendingPath); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static T? ReadJson<T>(string file) where T : class
    {
        try { return LocalTree.ReadOrNull(file) is { } bytes ? System.Text.Json.JsonSerializer.Deserialize<T>(bytes) : null; }
        catch (System.Text.Json.JsonException) { return null; }   // damaged = none: the worst case is a stale file taken for an edit
    }

    /// <summary>How long a trash folder is kept. ponytail: a constant; make it a setting if anyone asks.</summary>
    internal const int TrashKeepDays = 30;

    private const string TrashStampFormat = "yyyyMMdd-HHmmss";

    /// <summary>The folder one sync's trash copies go into (trash/&lt;yyyyMMdd-HHmmss&gt;).</summary>
    internal string TrashFolder(DateTime now) => Path.Combine(folder, "trash", now.ToString(TrashStampFormat));

    /// <summary>The folder one sync's backups go into (backup/&lt;yyyyMMdd-HHmmss&gt;).</summary>
    internal string BackupFolder(DateTime now) => Path.Combine(folder, "backup", now.ToString(BackupStampFormat));

    private const string BackupStampFormat = "yyyyMMdd-HHmmss";

    /// <summary>Refuses (IOException, so the caller skips the path) a target in the state folder that is reached through a link.</summary>
    private void RefuseLinks(string target)
    {
        if (LocalTree.ThroughLink(folder, target)) throw new IOException($"\"{target}\" is behind a link in the sync's own folder, so it was not written.");
    }

    private string TrashTarget(DateTime now, string relativePath)
    {
        string target = Path.Combine(TrashFolder(now), relativePath.Replace('/', Path.DirectorySeparatorChar));
        RefuseLinks(target);
        if (!File.Exists(target)) return target;
        // The same path trashed twice within one second: keep both, never overwrite.
        string stem = Path.Combine(Path.GetDirectoryName(target)!, Path.GetFileNameWithoutExtension(target)), extension = Path.GetExtension(target);
        for (int n = 2; ; n++)
            if (!File.Exists($"{stem} ({n}){extension}")) return $"{stem} ({n}){extension}";
    }

    /// <summary>Moves a local file that the sync removes into trash/&lt;yyyyMMdd-HHmmss&gt;/&lt;path&gt; and returns where it is now. Throws IOException or UnauthorizedAccessException when it cannot; the file is then still where it was.</summary>
    internal string MoveToTrash(DateTime now, string relativePath, string file)
    {
        string target = TrashTarget(now, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(file, target);
        return target;
    }

    /// <summary>Copies the local bytes of a file that the sync is about to overwrite into the trash and returns where the copy is. Throws like <see cref="MoveToTrash"/>.</summary>
    internal string CopyToTrash(DateTime now, string relativePath, byte[] bytes)
    {
        string target = TrashTarget(now, relativePath);
        LocalTree.WriteAtomic(target, bytes);
        return target;
    }

    /// <summary>Deletes the trash folders (the timestamp level only, by name) older than <see cref="TrashKeepDays"/>; anything else in there stays.</summary>
    internal void PruneTrash(DateTime now)
    {
        string trash = Path.Combine(folder, "trash");
        if (!Directory.Exists(trash)) return;
        DateTime limit = now.AddDays(-TrashKeepDays);
        foreach (string dir in Directory.EnumerateDirectories(trash))
        {
            if (!DateTime.TryParseExact(Path.GetFileName(dir), TrashStampFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime made) || made >= limit) continue;
            try { Directory.Delete(dir, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Copies a local file that is about to be replaced into backup/&lt;yyyyMMdd-HHmmss&gt;/&lt;path&gt; and returns where the copy is.</summary>
    internal string WriteBackup(DateTime now, string relativePath, byte[] bytes)
    {
        string target = Path.Combine(BackupFolder(now), relativePath.Replace('/', Path.DirectorySeparatorChar));
        RefuseLinks(target);
        LocalTree.WriteAtomic(target, bytes);
        return target;
    }
}
