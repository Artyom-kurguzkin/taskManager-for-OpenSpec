using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using BoardSync;
using BoardSync.PublicDTOs;
using BoardSync.Sync;
using BoardSync.Storage;

namespace TaskManager.Tests;

/// <summary>
/// An attacker's view of the board sync: a hostile storage (compromised bucket, a person with write access to the shared folder, a man in the middle),
/// hostile file names and contents in the board, a hostile response from the network. Every test states the SAFE behaviour; a failing test is a defect.
/// Temp folders and loopback only. No test has a wall-clock Timeout: the work runs on an injected clock, and a limit on the wall clock fails a correct test
/// while the run's first seconds keep every core busy. A test that hangs is named by the run settings' blame collector instead (tests.runsettings).
/// </summary>
public sealed class SecurityOfBoardSyncTests : IDisposable
{
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("rtm-sec-");
    private DateTime _now = new(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        try { _temp.Delete(true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ---- a computer and a storage ----------------------------------------------------------------------------------------------------

    private sealed record Site(string Dir, IBoardStorage Storage)
    {
        public string Root => Path.Combine(Dir, "changes");
        public string State => Path.Combine(Dir, "state");
        public string StorageFolder => Path.Combine(Dir, "storage");
    }

    private Site NewSite(string name)
    {
        string dir = Path.Combine(_temp.FullName, name);
        Directory.CreateDirectory(dir);
        return new Site(dir, BoardSyncApi.FolderStorage(Path.Combine(dir, "storage")));
    }

    private Task<SyncReport> Sync(Site s, bool allowMassDelete = false, IReadOnlySet<string>? edited = null, string? git = null) =>
        BoardSyncApi.Sync(new SyncOptions(s.Root, s.State, s.Storage, "dev", () => _now, allowMassDelete, edited, git));

    private void Write(Site s, string path, string text)
    {
        string file = Path.Combine(s.Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text, new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(file, _now);
    }

    private static string Hash(string text) => SyncModels.HashOf(Encoding.UTF8.GetBytes(text));

    /// <summary>A clock stamp the way an honest computer would write it, <paramref name="minutes"/> after the test's now.</summary>
    private string Clock(double minutes, string device = "attacker") =>
        $"{new DateTimeOffset(_now.AddMinutes(minutes)).ToUnixTimeMilliseconds():D15}-0000-{device}";

    private sealed record Hostile(string Text, string? Clock = null, bool Deleted = false, string[]? History = null, string? Hash = null, bool NoObject = false);

    /// <summary>What an attacker with write access to the storage can do: replace the manifest and plant objects, all consistent with each other.</summary>
    private async Task Publish(Site s, Dictionary<string, Hostile> entries)
    {
        ManifestFile manifest = new();
        foreach ((string path, Hostile h) in entries)
        {
            string hash = h.Hash ?? Hash(h.Text);
            if (!h.NoObject) await s.Storage.Put(SyncModels.ObjectPath(path, hash), Encoding.UTF8.GetBytes(h.Text), PutCondition.Always);
            manifest.Entries[path] = new ManifestEntry { Hash = hash, Clock = h.Clock ?? Clock(60), Deleted = h.Deleted, History = [.. h.History ?? []] };
        }
        await PublishRaw(s, SyncModels.Serialize(manifest));
    }

    private static Task PublishRaw(Site s, byte[] manifestBytes) => s.Storage.Put("manifest.json", manifestBytes, PutCondition.Always);

    private static ManifestFile Manifest(Site s) => SyncModels.ParseManifest(s.Storage.Get("manifest.json").GetAwaiter().GetResult()!.Bytes);

    private static string[] Files(string folder) => Directory.Exists(folder) ? Directory.GetFiles(folder, "*", SearchOption.AllDirectories) : [];

    private static bool AnyFileContains(string folder, string text) => Files(folder).Any(f => File.ReadAllText(f).Contains(text, StringComparison.Ordinal));

    private static void Junction(string link, string target)
    {
        using Process p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        p.WaitForExit();
        if (p.ExitCode != 0 || !Directory.Exists(link)) throw new InvalidOperationException("The test could not create a junction: " + p.StandardError.ReadToEnd());
    }

    private static async Task<T> Within<T>(Task<T> work, int seconds)
    {
        if (await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(seconds))) != work) throw new TimeoutException($"Still running after {seconds} s.");
        return await work;
    }

    // =====================================================================================================================================
    // 1. Where a sync may write: paths named by the manifest
    // =====================================================================================================================================

    [Fact(DisplayName = "a manifest path that leaves the board folder (dot dots, absolute, drive, UNC, backslash, stream) is refused and nothing is written outside")]
    public async Task Escaping_paths_are_refused()
    {
        string[] hostile = ["../escape.md", "a/../../escape.md", "/abs/escape.md", "C:/escape.md", "C:escape.md", "//server/share/escape.md", "\\\\server\\share\\escape.md",
            "a\\..\\..\\escape.md", "./escape.md", "a//b.md", "a/./b.md", "file.md:stream", "file.md::$DATA", ""];
        List<string> accepted = [];
        foreach (string path in hostile)
        {
            Site s = NewSite("p" + Math.Abs(path.GetHashCode()));
            await Publish(s, new() { [path] = new Hostile("payload") });
            SyncReport r = await Sync(s);
            if (r.Outcome != SyncOutcome.Failed) accepted.Add(path);
        }
        Assert.Empty(accepted);
        Assert.False(File.Exists(Path.Combine(_temp.FullName, "escape.md")));
    }

    [Fact(DisplayName = "a manifest path that is a Windows device name (CON, NUL.md, aux/x.md, COM1.txt) is refused")]
    public async Task Device_names_are_refused()
    {
        string[] hostile = ["CON", "NUL.md", "aux/x.md", "COM1.txt", "prn.md", "LPT1", "x/CONIN$"];
        List<string> accepted = [];
        foreach (string path in hostile)
        {
            Site s = NewSite("d" + Math.Abs(path.GetHashCode()));
            await Publish(s, new() { [path] = new Hostile("payload") });
            SyncReport r = await Sync(s);
            if (r.Outcome != SyncOutcome.Failed) accepted.Add(path);
        }
        Assert.Empty(accepted);
    }

    [Fact(DisplayName = "a manifest path with a trailing dot or space (which Windows silently renames) is refused")]
    public async Task Trailing_dot_and_space_are_refused()
    {
        string[] hostile = ["note.md.", "dir /x.md", "dir./x.md", "note.md "];
        List<string> accepted = [];
        foreach (string path in hostile)
        {
            Site s = NewSite("t" + Math.Abs(path.GetHashCode()));
            await Publish(s, new() { [path] = new Hostile("payload") });
            SyncReport r = await Sync(s);
            if (r.Outcome != SyncOutcome.Failed) accepted.Add(path);
        }
        Assert.Empty(accepted);
    }

    [Fact(DisplayName = "two manifest paths that differ only by case do not silently lose one of the files")]
    public async Task Case_collisions_do_not_lose_content()
    {
        Site s = NewSite("case");
        await Publish(s, new() { ["x/Note.md"] = new Hostile("first"), ["x/note.md"] = new Hostile("second") });
        SyncReport r = await Sync(s);
        string onDisk = string.Concat(Files(s.Root).Select(File.ReadAllText));
        bool both = onDisk.Contains("first") && onDisk.Contains("second");
        Assert.True(r.Outcome == SyncOutcome.Failed || both, $"Outcome {r.Outcome}, the board folder holds: {onDisk}");
    }

    [Fact(DisplayName = "a manifest path far longer than the file system allows ends the sync as Failed, not as an exception")]
    public async Task Overlong_paths_fail_cleanly()
    {
        Site s = NewSite("long");
        await Publish(s, new() { [string.Join('/', Enumerable.Repeat(new string('d', 100), 6)) + "/x.md"] = new Hostile("payload") });
        Exception? thrown = await Record.ExceptionAsync(() => Sync(s));
        Assert.Null(thrown);
    }

    [Fact(DisplayName = "a junction inside the board folder is not followed: files behind it are not uploaded")]
    public async Task Junction_is_not_read()
    {
        Site s = NewSite("jr");
        string outside = Path.Combine(_temp.FullName, "outside-r");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "TOP SECRET OUTSIDE THE BOARD");
        Directory.CreateDirectory(s.Root);
        Junction(Path.Combine(s.Root, "link"), outside);
        Write(s, "ok/proposal.md", "fine");
        await Sync(s);
        Assert.DoesNotContain(Manifest(s).Entries.Keys, k => k.StartsWith("link/", StringComparison.Ordinal));
        Assert.False(AnyFileContains(s.StorageFolder, "TOP SECRET OUTSIDE THE BOARD"));
    }

    [Fact(DisplayName = "a junction inside the board folder is not followed: a manifest path through it writes nothing outside the board")]
    public async Task Junction_is_not_written_through()
    {
        Site s = NewSite("jw");
        string outside = Path.Combine(_temp.FullName, "outside-w");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(s.Root);
        Junction(Path.Combine(s.Root, "link"), outside);
        await Publish(s, new() { ["link/planted.md"] = new Hostile("planted by the storage") });
        await Sync(s);
        Assert.Empty(Files(outside));
    }

    [Fact(DisplayName = "a junction inside the board folder is not followed: a tombstone through it does not move a file outside the board away")]
    public async Task Junction_is_not_deleted_through()
    {
        Site s = NewSite("jd");
        string outside = Path.Combine(_temp.FullName, "outside-d");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "victim.txt"), "victim");
        Directory.CreateDirectory(s.Root);
        Junction(Path.Combine(s.Root, "link"), outside);
        await Publish(s, new() { ["link/victim.txt"] = new Hostile("victim", Clock(1)) });
        await Sync(s);
        _now = _now.AddMinutes(10);
        await Publish(s, new() { ["link/victim.txt"] = new Hostile("victim", Clock(30), Deleted: true) });
        await Sync(s);
        Assert.True(File.Exists(Path.Combine(outside, "victim.txt")), "the file outside the board was moved into the trash by a sync");
    }

    // =====================================================================================================================================
    // 2. Integrity of what is downloaded, and of the manifest itself
    // =====================================================================================================================================

    [Fact(DisplayName = "an object whose bytes do not hash to the manifest's content hash (different, empty or truncated) is never written into the board")]
    public async Task Object_bytes_are_checked_against_the_manifest()
    {
        foreach (byte[] served in new[] { Encoding.UTF8.GetBytes("other content"), [], Encoding.UTF8.GetBytes("genuin") })
        {
            Site s = NewSite("i" + served.Length + served.GetHashCode());
            string hash = Hash("genuine content");
            await Publish(s, new() { ["a/proposal.md"] = new Hostile("genuine content", Hash: hash, NoObject: true) });
            await s.Storage.Put(SyncModels.ObjectPath("a/proposal.md", hash), served, PutCondition.Always);
            SyncReport r = await Sync(s);
            Assert.Equal(SyncOutcome.Failed, r.Outcome);
            Assert.Empty(Files(s.Root));
        }
    }

    [Fact(DisplayName = "a manifest that is empty, null, an array, text, has a wrong version or a huge number is refused without touching the board")]
    public async Task Garbled_manifests_are_refused()
    {
        string[] bodies = ["", "null", "[]", "\"x\"", "{", "{\"version\":2,\"entries\":{}}", "{\"version\":99999999999999999999}", "{\"version\":1,\"entries\":{\"a.md\":5}}"];
        foreach (string body in bodies)
        {
            Site s = NewSite("g" + Math.Abs(body.GetHashCode()));
            Write(s, "keep/proposal.md", "mine");
            await PublishRaw(s, Encoding.UTF8.GetBytes(body));
            Exception? thrown = await Record.ExceptionAsync(async () => Assert.Equal(SyncOutcome.Failed, (await Sync(s)).Outcome));
            Assert.True(thrown is null, $"manifest {body}: {thrown}");
            Assert.Equal("mine", File.ReadAllText(Path.Combine(s.Root, "keep/proposal.md")));
        }
    }

    [Fact(DisplayName = "a manifest whose entries are null ends the sync as Failed, not as an exception")]
    public async Task Null_entries_do_not_crash()
    {
        Site s = NewSite("ne");
        await PublishRaw(s, Encoding.UTF8.GetBytes("{\"version\":1,\"entries\":null}"));
        SyncReport? report = null;
        Exception? thrown = await Record.ExceptionAsync(async () => report = await Sync(s));
        Assert.True(thrown is null, thrown?.ToString());
        Assert.Equal(SyncOutcome.Failed, report!.Outcome);
    }

    [Fact(DisplayName = "a manifest entry whose hash is null ends the sync as Failed, not as an exception")]
    public async Task Null_hash_does_not_crash()
    {
        Site s = NewSite("nh");
        await PublishRaw(s, Encoding.UTF8.GetBytes("{\"version\":1,\"entries\":{\"a/proposal.md\":{\"hash\":null,\"clock\":\"x\",\"deleted\":false}}}"));
        SyncReport? report = null;
        Exception? thrown = await Record.ExceptionAsync(async () => report = await Sync(s));
        Assert.True(thrown is null, thrown?.ToString());
        Assert.Equal(SyncOutcome.Failed, report!.Outcome);
    }

    [Fact(DisplayName = "a malformed content hash (too short, not hex) in the manifest is refused and nothing is written")]
    public async Task Malformed_hashes_are_refused()
    {
        foreach (string hash in new[] { "ab", "zz11", "../../x", new string('a', 70) })
        {
            Site s = NewSite("h" + hash.Length);
            await Publish(s, new() { ["a/proposal.md"] = new Hostile("x", Hash: hash, NoObject: true) });
            Assert.Equal(SyncOutcome.Failed, (await Sync(s)).Outcome);
            Assert.Empty(Files(s.Root));
        }
    }

    // =====================================================================================================================================
    // 3. Destruction by a hostile storage
    // =====================================================================================================================================

    [Fact(DisplayName = "a hostile storage cannot delete most of the board by tombstoning just under the brake in every sync", Timeout = 60000)]
    public async Task Deletion_brake_is_cumulative()
    {
        Site s = NewSite("brake");
        for (int i = 0; i < 100; i++) Write(s, $"w{i:D3}/proposal.md", $"work item {i}");
        await Sync(s);
        bool blocked = false;
        for (int round = 0; round < 8 && !blocked; round++)
        {
            _now = _now.AddMinutes(5);
            ManifestFile m = Manifest(s);
            List<string> live = m.Entries.Where(e => !e.Value.Deleted).Select(e => e.Key).Order(StringComparer.Ordinal).ToList();
            int limit = Math.Max(10, (int)Math.Ceiling(live.Count * 0.3));
            foreach (string path in live.Take(limit)) { m.Entries[path].Deleted = true; m.Entries[path].Clock = Clock(round * 5 + 1000); }
            await PublishRaw(s, SyncModels.Serialize(m));
            blocked = (await Sync(s)).Outcome == SyncOutcome.Blocked;
        }
        int left = Files(s.Root).Count(f => f.EndsWith("proposal.md", StringComparison.Ordinal));
        Assert.True(blocked || left >= 50, $"{left} of 100 files left and no sync was blocked");
    }

    [Fact(DisplayName = "content that a download replaces on this computer stays recoverable in the state folder")]
    public async Task Overwritten_content_is_kept()
    {
        Site s = NewSite("over");
        for (int i = 0; i < 5; i++) Write(s, $"w{i}/proposal.md", $"precious local text number {i}");
        await Sync(s);
        _now = _now.AddMinutes(5);
        await Publish(s, Enumerable.Range(0, 5).ToDictionary(i => $"w{i}/proposal.md", i => new Hostile($"replaced by the storage {i}", Clock(30))));
        SyncReport r = await Sync(s);
        Assert.Equal(5, r.Downloaded.Count);
        for (int i = 0; i < 5; i++) Assert.True(AnyFileContains(s.State, $"precious local text number {i}"), $"the old text of w{i} is gone");
    }

    [Fact(DisplayName = "a storage cannot make a local edit 'stale' by listing its hash in the history and so overwrite it without a copy")]
    public async Task Forged_history_does_not_destroy_a_local_edit()
    {
        Site s = NewSite("stale");
        Write(s, "w/proposal.md", "version one");
        await Sync(s);
        _now = _now.AddMinutes(5);
        Write(s, "w/proposal.md", "MY IMPORTANT UNSAVED EDIT made in git");
        await Publish(s, new() { ["w/proposal.md"] = new Hostile("hostile", Clock(60), History: [Hash("MY IMPORTANT UNSAVED EDIT made in git"), Hash("version one")]) });
        await Sync(s, edited: new HashSet<string>());
        bool kept = File.ReadAllText(Path.Combine(s.Root, "w/proposal.md")).Contains("MY IMPORTANT") || AnyFileContains(s.State, "MY IMPORTANT UNSAVED EDIT");
        Assert.True(kept, "the edit is gone from the board and from the state folder");
    }

    [Fact(DisplayName = "a manifest clock at the top of the range does not make this computer issue a smaller clock for the next edit")]
    public void Clock_after_the_maximum_still_increases()
    {
        string floor = "999999999999999-9999-zzz";
        string next = SyncClock.After("000001762934400000-0000-dev", floor, "dev");
        Assert.True(SyncClock.IsLater(next, floor), $"{next} is not later than {floor}");
    }

    [Fact(DisplayName = "an edit made after the storage entry got a maximal clock is uploaded with a later clock")]
    public async Task Edit_after_a_maximal_clock_gets_a_later_clock()
    {
        Site s = NewSite("clk");
        string floor = "999999999999999-9999-zzz";
        string clamped = Clock(0, "");       // a clock more than a day ahead counts as "now" (decision 4), so the floor for the next clock is this
        await Publish(s, new() { ["w/proposal.md"] = new Hostile("one", floor) });
        await Sync(s);
        _now = _now.AddMinutes(5);
        Write(s, "w/proposal.md", "two");
        await Sync(s);
        string now = Manifest(s).Entries["w/proposal.md"].Clock;
        Assert.True(SyncClock.IsLater(now, clamped), $"the manifest holds {now} after a local edit, not later than {clamped}");
    }

    [Fact(DisplayName = "a manifest clock far in the future does not make the storage win every clash for good")]
    public async Task Far_future_clock_does_not_win_every_clash()
    {
        Site s = NewSite("skew");
        Write(s, "w/proposal.md", "alpha\nmiddle\nomega\n");
        await Sync(s);
        _now = _now.AddMinutes(5);
        Write(s, "w/proposal.md", "alpha\nLOCAL EDIT\nomega\n");
        await Publish(s, new() { ["w/proposal.md"] = new Hostile("alpha\nREMOTE EDIT\nomega\n", "999999999999999-0000-zzz") });
        await Sync(s);
        Assert.Contains("LOCAL EDIT", File.ReadAllText(Path.Combine(s.Root, "w/proposal.md")));
    }

    [Fact(DisplayName = "a tombstone with a far-future clock does not delete a file edited here a moment ago")]
    public async Task Far_future_tombstone_does_not_delete_an_edit()
    {
        Site s = NewSite("skewdel");
        Write(s, "w/proposal.md", "one");
        await Sync(s);
        _now = _now.AddMinutes(5);
        Write(s, "w/proposal.md", "two, edited just now");
        await Publish(s, new() { ["w/proposal.md"] = new Hostile("one", "999999999999999-0000-zzz", Deleted: true) });
        await Sync(s);
        Assert.True(File.Exists(Path.Combine(s.Root, "w/proposal.md")));
    }

    // =====================================================================================================================================
    // 4. Resource exhaustion
    // =====================================================================================================================================

    [Fact(DisplayName = "a manifest entry with a history of 100000 hashes is cut to a bounded size before it is saved", Timeout = 60000)]
    public async Task Unbounded_history_is_cut()
    {
        Site s = NewSite("hist");
        string[] history = Enumerable.Range(0, 100_000).Select(i => Hash("h" + i)).ToArray();
        await Publish(s, new() { ["w/proposal.md"] = new Hostile("content", History: history) });
        Assert.Equal(SyncOutcome.Synced, (await Sync(s)).Outcome);
        long state = new FileInfo(Path.Combine(s.State, "state.json")).Length;
        long seen = new FileInfo(Path.Combine(s.State, "seen.json")).Length;
        Assert.True(state < 100_000 && seen < 100_000, $"state.json is {state} bytes and seen.json {seen} bytes after one sync");
    }

    private sealed class Endless(long limit) : Stream
    {
        public long Served { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => ReadSpan(buffer.AsSpan(offset, count));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(ReadSpan(buffer.Span));
        private int ReadSpan(Span<byte> span)
        {
            int n = (int)Math.Min(span.Length, limit - Served);
            Served += n;
            return n;
        }
    }

    private sealed class FuncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return handle(request);
        }
    }

    private static IBoardStorage S3(HttpMessageHandler handler, string endpoint = "https://storage.example.test", string? prefix = null) =>
        BoardSyncApi.S3Storage(endpoint, "board-bucket", prefix, "AKIATESTACCESSKEYID", "SECRETKEYVALUE/never+leak", "auto", handler);

    [Fact(DisplayName = "a storage answering a download with an endless body is stopped before tens of megabytes are read into memory", Timeout = 60000)]
    public async Task Response_size_is_bounded()
    {
        Endless body = new(40_000_000);
        FuncHandler h = new(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }));
        IBoardStorage storage = S3(h);
        await Record.ExceptionAsync(() => storage.Get("manifest.json"));
        Assert.True(body.Served < 40_000_000, $"the client read all {body.Served} bytes of the response into memory");
    }

    [Fact(DisplayName = "a bucket listing that is truncated for ever with the same token is given up on after a few pages", Timeout = 60000)]
    public async Task Listing_loop_is_bounded()
    {
        const string page = "<?xml version=\"1.0\"?><ListBucketResult><IsTruncated>true</IsTruncated><NextContinuationToken>same</NextContinuationToken><Contents><Key>objects/a</Key><ETag>\"x\"</ETag></Contents></ListBucketResult>";
        FuncHandler h = null!;
        h = new FuncHandler(_ => h.Requests > 500 ? throw new HttpRequestException("stop") : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(page) }));
        await Record.ExceptionAsync(() => S3(h).List(""));
        Assert.True(h.Requests < 500, $"the listing asked for {h.Requests} pages of the same token and was only stopped from outside");
    }

    [Fact(DisplayName = "a bucket listing with a DOCTYPE (entity declarations) is refused rather than parsed")]
    public async Task Listing_with_a_dtd_is_refused()
    {
        const string xml = "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY a \"expanded\">]><ListBucketResult><IsTruncated>false</IsTruncated><Contents><Key>&a;</Key><ETag>\"x\"</ETag></Contents></ListBucketResult>";
        FuncHandler h = new(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) }));
        await Assert.ThrowsAsync<BoardStorageException>(() => S3(h).List(""));
    }

    [Fact(DisplayName = "a billion-laughs bucket listing ends quickly as a storage error", Timeout = 30000)]
    public async Task Entity_expansion_is_bounded()
    {
        StringBuilder dtd = new("<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY l0 \"lollollollollollollollollollol\">");
        for (int i = 1; i < 9; i++) dtd.Append($"<!ENTITY l{i} \"{string.Concat(Enumerable.Repeat($"&l{i - 1};", 10))}\">");
        dtd.Append("]><ListBucketResult><IsTruncated>false</IsTruncated><Contents><Key>&l8;</Key><ETag>\"x\"</ETag></Contents></ListBucketResult>");
        FuncHandler h = new(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(dtd.ToString()) }));
        Stopwatch watch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<BoardStorageException>(() => Within(S3(h).List(""), 20));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
    }

    [Fact(DisplayName = "an external entity in a bucket listing does not read a local file into a key")]
    public async Task External_entities_are_not_resolved()
    {
        string secret = Path.Combine(_temp.FullName, "xxe-secret.txt");
        File.WriteAllText(secret, "LOCAL FILE CONTENT");
        string xml = $"<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY x SYSTEM \"{new Uri(secret).AbsoluteUri}\">]><ListBucketResult><IsTruncated>false</IsTruncated><Contents><Key>&x;</Key><ETag>\"e\"</ETag></Contents></ListBucketResult>";
        FuncHandler h = new(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) }));
        IReadOnlyList<StoredEntry>? entries = null;
        await Record.ExceptionAsync(async () => entries = await S3(h).List(""));
        Assert.DoesNotContain(entries ?? [], e => e.Path.Contains("LOCAL FILE CONTENT", StringComparison.Ordinal));
    }

    // =====================================================================================================================================
    // 5. S3 transport and signing
    // =====================================================================================================================================

    [Fact(DisplayName = "a plain http endpoint (clear text, no TLS) is refused for a remote host")]
    public void Plain_http_is_refused()
    {
        Assert.Throws<BoardStorageException>(() => S3(new FuncHandler(_ => throw new InvalidOperationException()), "http://storage.example.test"));
    }

    private sealed class RawServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public List<string> Requests { get; } = [];
        private readonly Func<RawServer, string> _answer;

        public RawServer(Func<RawServer, string> answer)
        {
            _answer = answer;
            _listener.Start();
            _ = Task.Run(Loop);
        }

        private async Task Loop()
        {
            while (true)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(); } catch (Exception) { return; }
                _ = Task.Run(async () =>
                {
                    using (client)
                    {
                        try
                        {
                            NetworkStream stream = client.GetStream();
                            string head = await ReadHead(stream);
                            lock (Requests) Requests.Add(head);
                            byte[] reply = Encoding.UTF8.GetBytes(_answer(this));
                            await stream.WriteAsync(reply);
                            await stream.FlushAsync();
                        }
                        catch (Exception) { }
                    }
                });
            }
        }

        private static async Task<string> ReadHead(Stream stream)
        {
            StringBuilder text = new();
            byte[] one = new byte[1];
            while (!text.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal) && await stream.ReadAsync(one) == 1) text.Append((char)one[0]);
            string head = text.ToString();
            int length = head.Split("\r\n").Where(l => l.StartsWith("content-length:", StringComparison.OrdinalIgnoreCase)).Select(l => int.Parse(l[15..].Trim())).FirstOrDefault();
            byte[] body = new byte[length];
            int read = 0;
            while (read < length) { int n = await stream.ReadAsync(body.AsMemory(read)); if (n == 0) break; read += n; }
            return head + Encoding.UTF8.GetString(body, 0, read);
        }

        public void Dispose() => _listener.Stop();
    }

    private static IBoardStorage RealNetworkS3(int port, string? prefix = null) =>
        BoardSyncApi.S3Storage($"http://127.0.0.1:{port}", "board-bucket", prefix, "AKIATESTACCESSKEYID", "SECRETKEYVALUE/never+leak");

    [Fact(DisplayName = "a redirect from the storage to another host does not carry the Authorization header along", Timeout = 30000)]
    public async Task Redirect_does_not_forward_authorization()
    {
        using RawServer target = new(_ => "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using RawServer origin = new(_ => $"HTTP/1.1 307 Temporary Redirect\r\nLocation: http://127.0.0.1:{target.Port}/elsewhere\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await Record.ExceptionAsync(() => RealNetworkS3(origin.Port).Get("manifest.json"));
        await Task.Delay(300);
        string got = string.Join("\n", target.Requests);
        Assert.DoesNotContain("authorization", got, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "a redirect from the storage is not followed: board content is not re-sent to the host named in Location", Timeout = 30000)]
    public async Task Redirect_is_not_followed()
    {
        using RawServer target = new(_ => "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using RawServer origin = new(_ => $"HTTP/1.1 307 Temporary Redirect\r\nLocation: http://127.0.0.1:{target.Port}/elsewhere\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await Record.ExceptionAsync(() => RealNetworkS3(origin.Port).Put("objects/ab/cd", Encoding.UTF8.GetBytes("BOARD CONTENT"), PutCondition.Always));
        await Task.Delay(300);
        Assert.DoesNotContain(target.Requests, r => r.Contains("BOARD CONTENT", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "a server with a certificate nobody trusts is refused and receives no request", Timeout = 30000)]
    public async Task Untrusted_certificate_is_refused()
    {
        using RSA rsa = RSA.Create(2048);
        CertificateRequest request = new("CN=127.0.0.1", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        SubjectAlternativeNameBuilder san = new();
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        using X509Certificate2 selfSigned = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using X509Certificate2 cert = X509CertificateLoader.LoadPkcs12(selfSigned.Export(X509ContentType.Pfx), null);

        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int requestsSeen = 0;
        Task server = Task.Run(async () =>
        {
            try
            {
                using TcpClient client = await listener.AcceptTcpClientAsync();
                using SslStream ssl = new(client.GetStream());
                await ssl.AuthenticateAsServerAsync(cert);
                byte[] buffer = new byte[4096];
                if (await ssl.ReadAsync(buffer) > 0) requestsSeen++;
            }
            catch (Exception) { }
        });
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        IBoardStorage storage = BoardSyncApi.S3Storage($"https://127.0.0.1:{port}", "board-bucket", null, "AKIATESTACCESSKEYID", "SECRETKEYVALUE/never+leak");
        await Assert.ThrowsAsync<BoardStorageException>(() => storage.Get("manifest.json"));
        await Task.WhenAny(server, Task.Delay(500));
        listener.Stop();
        Assert.Equal(0, requestsSeen);
    }

    [Fact(DisplayName = "no storage error message, exception text or ToString contains the secret key or a signature", Timeout = 30000)]
    public async Task Secret_never_appears_in_errors()
    {
        const string secret = "SECRETKEYVALUE/never+leak";
        List<string> texts = [];
        foreach (HttpStatusCode status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.InternalServerError, HttpStatusCode.NotFound, HttpStatusCode.BadRequest })
        {
            FuncHandler h = new(req => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent($"<Error><Code>X</Code><Message>{req.Headers.Authorization}{string.Join(",", req.Headers.Select(x => string.Join(",", x.Value)))}</Message></Error>") }));
            IBoardStorage storage = S3(h);
            texts.Add(storage.ToString() ?? "");
            Exception? e1 = await Record.ExceptionAsync(() => storage.Get("manifest.json"));
            Exception? e2 = await Record.ExceptionAsync(() => storage.Put("manifest.json", [1], PutCondition.Always));
            Exception? e3 = await Record.ExceptionAsync(() => storage.List(""));
            texts.AddRange(new[] { e1, e2, e3 }.Select(e => e?.ToString() ?? ""));
        }
        FuncHandler broken = new(_ => throw new HttpRequestException("connection refused"));
        Exception? e4 = await Record.ExceptionAsync(() => S3(broken).Get("manifest.json"));
        texts.Add(e4?.ToString() ?? "");
        foreach (string text in texts)
        {
            Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
            Assert.DoesNotContain("Signature=", text, StringComparison.Ordinal);
        }
    }

    [Fact(DisplayName = "credentials typed into the endpoint address (https://key:secret@host) never appear in an error message", Timeout = 30000)]
    public async Task Userinfo_in_the_endpoint_does_not_leak()
    {
        FuncHandler broken = new(_ => throw new HttpRequestException("connection refused"));
        IBoardStorage? storage = null;
        Exception? e = Record.Exception(() => storage = S3(broken, "https://AKIAUSER:HUNTER2SECRET@storage.example.test"));
        if (e is null) e = await Record.ExceptionAsync(() => storage!.Get("manifest.json"));
        Assert.NotNull(e);
        Assert.DoesNotContain("HUNTER2SECRET", e!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("AKIAUSER", e.ToString(), StringComparison.Ordinal);
        Exception? bad = Record.Exception(() => S3(broken, "ftp://AKIAUSER:HUNTER2SECRET@storage.example.test"));
        Assert.DoesNotContain("HUNTER2SECRET", bad?.ToString() ?? "", StringComparison.Ordinal);
    }

    [Fact(DisplayName = "keys with spaces, plus, percent, question mark, hash and non-ASCII, under prefixes with slashes, are stored and read back with valid signatures", Timeout = 30000)]
    public async Task Odd_keys_are_signed_correctly()
    {
        foreach (string prefix in new[] { "", "/lead/", "trail/", " spaced / double/slash ", "ü/ä" })
        {
            FakeS3 fake = new();
            IBoardStorage storage = BoardSyncApi.S3Storage("https://storage.example.test", FakeS3.Bucket, prefix, FakeS3.AccessKey, FakeS3.SecretKey, FakeS3.Region, fake);
            foreach (string key in new[] { "objects/a b/c+d%e.md", "x?y#z/w&v=1", "ünï/cødé €.md", "a/~tilde/!*'().md", "100%/%2F/%2e%2e/x" })
            {
                byte[] bytes = Encoding.UTF8.GetBytes("content of " + key);
                PutResult put = await storage.Put(key, bytes, PutCondition.OnlyIfAbsent);
                Assert.True(put.Written, $"put {key} under '{prefix}'");
                StoredFile? back = await storage.Get(key);
                Assert.Equal(bytes, back?.Bytes);
            }
            Assert.All(fake.Objects.Keys, k => Assert.StartsWith(FakeS3.Bucket + "/" + prefix.Trim().Trim('/') + (prefix.Trim().Trim('/').Length > 0 ? "/" : ""), k, StringComparison.Ordinal));
        }
    }

    [Fact(DisplayName = "a prefix with an empty part (a double slash) is refused when the storage is built")]
    public void Prefix_with_an_empty_part_is_refused()
    {
        FuncHandler h = new(_ => throw new InvalidOperationException());
        Assert.Throws<BoardStorageException>(() => S3(h, prefix: " spaced / double//slash "));
        Assert.Throws<BoardStorageException>(() => S3(h, prefix: "a//b"));
        Assert.NotNull(S3(h, prefix: "/lead/a/b/"));
    }

    [Fact(DisplayName = "a prefix with dot-dot segments cannot lead requests out of the configured bucket")]
    public async Task Prefix_cannot_climb_out_of_the_bucket()
    {
        List<string> paths = [];
        FuncHandler h = new(req =>
        {
            paths.Add(req.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });
        Assert.Throws<BoardStorageException>(() => S3(h, prefix: "../victim-bucket/stolen"));
        await Task.CompletedTask;
        Assert.All(paths, p => Assert.StartsWith("/board-bucket/", p, StringComparison.Ordinal));
    }

    // =====================================================================================================================================
    // 6. Folder provider
    // =====================================================================================================================================

    [Fact(DisplayName = "a junction inside the storage folder does not make a write land outside the storage folder")]
    public async Task Storage_junction_is_not_followed()
    {
        string storageFolder = Path.Combine(_temp.FullName, "shared");
        string outside = Path.Combine(_temp.FullName, "elsewhere");
        Directory.CreateDirectory(storageFolder);
        Directory.CreateDirectory(outside);
        Junction(Path.Combine(storageFolder, "objects"), outside);
        IBoardStorage storage = BoardSyncApi.FolderStorage(storageFolder);
        Exception? refused = await Record.ExceptionAsync(() => storage.Put("objects/ab/cdef", [1, 2, 3], PutCondition.Always));
        Assert.IsType<BoardStorageException>(refused);
        Assert.Empty(Files(outside));
    }

    [Fact(DisplayName = "a storage folder lock held by someone else ends a write as a storage error after the timeout, not a hang", Timeout = 70000)]
    public async Task Held_lock_times_out()
    {
        string folder = Path.Combine(_temp.FullName, "locked");
        Directory.CreateDirectory(folder);
        using FileStream holder = new(Path.Combine(folder, ".rtm-storage.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using IDisposable shortWait = FolderStorage.OverrideLockTimeout(TimeSpan.FromSeconds(2));   // the product waits 30 s; the behaviour is the same at any length
        IBoardStorage storage = BoardSyncApi.FolderStorage(folder);
        Stopwatch watch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<BoardStorageException>(() => Within(storage.Put("manifest.json", [1], PutCondition.Always), 20));
        Assert.True(watch.Elapsed >= TimeSpan.FromSeconds(1.5), $"gave up after only {watch.Elapsed}");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"took {watch.Elapsed}");
    }

    [Fact(DisplayName = "a write to a storage folder leaves no temp file behind when it is refused", Timeout = 30000)]
    public async Task Refused_write_leaves_no_temp_file()
    {
        string folder = Path.Combine(_temp.FullName, "tmpcheck");
        IBoardStorage storage = BoardSyncApi.FolderStorage(folder);
        await storage.Put("manifest.json", [1], PutCondition.OnlyIfAbsent);
        PutResult second = await storage.Put("manifest.json", [2], PutCondition.OnlyIfAbsent);
        Assert.False(second.Written);
        Assert.DoesNotContain(Files(folder), f => f.EndsWith(".rtm-tmp", StringComparison.Ordinal));
    }

    // =====================================================================================================================================
    // 7. Git guard
    // =====================================================================================================================================

    [Fact(DisplayName = "a board file that only quotes conflict markers (inside a code fence) does not pause sync for good")]
    public async Task Quoted_markers_do_not_block_sync()
    {
        Site s = NewSite("git");
        string git = Path.Combine(s.Dir, ".git");
        Directory.CreateDirectory(git);
        string doc = "# How conflicts look\n\n```\n<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> branch\n```\n";
        await Publish(s, new() { ["w/design.md"] = new Hostile(doc) });
        await Sync(s, git: git);
        SyncReport second = await Sync(s, git: git);
        Assert.NotEqual(SyncOutcome.Blocked, second.Outcome);
        Assert.Null(BoardSyncApi.GitPause(s.Root, git));
    }

    // =====================================================================================================================================
    // 9. What the fixes added
    // =====================================================================================================================================

    [Fact(DisplayName = "files a download replaced are listed as Replaced (copies in the trash), not as Trashed, and a new file has no copy")]
    public async Task Replaced_list_is_separate_from_trashed()
    {
        Site s = NewSite("replaced");
        Write(s, "w/proposal.md", "local text");
        await Sync(s);
        _now = _now.AddMinutes(5);
        await Publish(s, new() { ["w/proposal.md"] = new Hostile("storage text", Clock(30)), ["w/new.md"] = new Hostile("brand new", Clock(30)) });
        SyncReport r = await Sync(s);
        Assert.Equal(["w/new.md", "w/proposal.md"], r.Downloaded.Order(StringComparer.Ordinal));
        string copy = Assert.Single(r.Replaced);
        Assert.Equal("local text", File.ReadAllText(copy));
        Assert.StartsWith(Path.Combine(s.State, "trash") + Path.DirectorySeparatorChar, copy);
        Assert.Empty(r.Trashed);
        Assert.Empty(r.DeletedHere);
    }

    [Fact(DisplayName = "deletions are counted over 24 hours: a second small deletion that passes the ceiling is blocked, and an allowed sync resets the window", Timeout = 60000)]
    public async Task Deletion_window_is_kept_and_reset()
    {
        Site s = NewSite("window");
        for (int i = 0; i < 40; i++) Write(s, $"w{i:D2}/proposal.md", $"item {i}");
        await Sync(s);

        async Task<SyncReport> TombstoneAndSync(int count)
        {
            _now = _now.AddMinutes(10);
            ManifestFile m = Manifest(s);
            foreach (string path in m.Entries.Where(e => !e.Value.Deleted).Select(e => e.Key).Order(StringComparer.Ordinal).Take(count))
            { m.Entries[path].Deleted = true; m.Entries[path].Clock = Clock(2000); }
            await PublishRaw(s, SyncModels.Serialize(m));
            return await Sync(s);
        }

        Assert.Equal(SyncOutcome.Synced, (await TombstoneAndSync(12)).Outcome);                     // 12 of 40 is the ceiling
        Assert.True(File.Exists(Path.Combine(s.State, "deletions.json")));
        SyncReport blocked = await TombstoneAndSync(5);
        Assert.Equal(SyncOutcome.Blocked, blocked.Outcome);
        Assert.Equal(SyncBlock.MassDelete, blocked.Blocked);
        Assert.Equal(28, Files(s.Root).Length);                                                     // nothing more was deleted

        SyncReport allowed = await Sync(s, allowMassDelete: true);
        Assert.Equal(SyncOutcome.Synced, allowed.Outcome);
        Assert.False(File.Exists(Path.Combine(s.State, "deletions.json")), "an allowed sync starts the window again");
        Assert.Equal(SyncOutcome.Synced, (await TombstoneAndSync(3)).Outcome);
    }

    [Fact(DisplayName = "deletions older than 24 hours no longer count towards the brake", Timeout = 60000)]
    public async Task Deletion_window_expires()
    {
        Site s = NewSite("expire");
        for (int i = 0; i < 40; i++) Write(s, $"w{i:D2}/proposal.md", $"item {i}");
        await Sync(s);
        for (int round = 0; round < 2; round++)
        {
            _now = _now.AddHours(25);
            ManifestFile m = Manifest(s);
            foreach (string path in m.Entries.Where(e => !e.Value.Deleted).Select(e => e.Key).Order(StringComparer.Ordinal).Take(10)) { m.Entries[path].Deleted = true; m.Entries[path].Clock = Clock(round * 3000 + 3000); }
            await PublishRaw(s, SyncModels.Serialize(m));
            Assert.Equal(SyncOutcome.Synced, (await Sync(s)).Outcome);
        }
    }

    [Fact(DisplayName = "http is accepted for localhost, 127.0.0.1 and ::1 only, and an endpoint or prefix with a query, fragment or dot parts is refused")]
    public void Http_is_allowed_for_loopback_only()
    {
        FuncHandler h = new(_ => throw new InvalidOperationException());
        foreach (string ok in new[] { "http://localhost:9000", "http://127.0.0.1:9000", "http://[::1]:9000" }) Assert.NotNull(S3(h, ok));
        foreach (string bad in new[] { "http://example.test", "http://10.0.0.5:9000", "http://localhost.example.test", "ftp://localhost" })
            Assert.Throws<BoardStorageException>(() => S3(h, bad));
        foreach (string bad in new[] { "https://host.test?x=1", "https://host.test#frag" }) Assert.Throws<BoardStorageException>(() => S3(h, bad));
        Assert.Throws<BoardStorageException>(() => S3(h, prefix: "a/../b"));
        Assert.Throws<BoardStorageException>(() => S3(h, prefix: "./a"));
    }

    [Fact(DisplayName = "any 3xx answer from the storage is a storage error that does not name the target, and 304 still means not modified")]
    public async Task Redirect_answers_are_errors()
    {
        FuncHandler h = new(_ =>
        {
            HttpResponseMessage r = new(HttpStatusCode.TemporaryRedirect);
            r.Headers.Location = new Uri("https://attacker.example/steal");
            return Task.FromResult(r);
        });
        BoardStorageException e = await Assert.ThrowsAsync<BoardStorageException>(() => S3(h).Get("manifest.json"));
        Assert.Contains("redirect", e.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attacker.example", e.Message, StringComparison.Ordinal);
        FuncHandler notModified = new(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified)));
        Assert.True((await S3(notModified).GetIfChanged("manifest.json", "\"v\"")) is { IsNotModified: true });
    }

    [Fact(DisplayName = "the access key id is shown with its last 4 characters only in a refused-key message")]
    public async Task Access_key_is_masked()
    {
        FuncHandler h = new(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));
        BoardStorageException e = await Assert.ThrowsAsync<BoardStorageException>(() => S3(h).Get("manifest.json"));
        Assert.DoesNotContain("AKIATESTACCESSKEYID", e.Message, StringComparison.Ordinal);
        Assert.Contains("EYID", e.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a link in the board folder is reported as skipped, nothing is written behind it, and the storage's paths under it are neither downloaded nor tombstoned", Timeout = 30000)]
    public async Task Links_are_skipped_in_both_directions()
    {
        Site s = NewSite("links");
        string outside = Path.Combine(_temp.FullName, "outside-links");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "own.txt"), "not the board's");
        Directory.CreateDirectory(s.Root);
        Junction(Path.Combine(s.Root, "link"), outside);
        Write(s, "w/proposal.md", "board file");
        await Publish(s, new() { ["link/planted.md"] = new Hostile("planted"), ["w/other.md"] = new Hostile("other") });
        SyncReport first = await Sync(s);
        Assert.Contains("link", first.SkippedLinks);
        Assert.Equal(["own.txt"], Files(outside).Select(Path.GetFileName));
        Assert.Contains("w/other.md", first.Downloaded);
        for (int round = 0; round < 2; round++)
        {
            _now = _now.AddMinutes(5);
            await Sync(s);
            ManifestFile m = Manifest(s);
            Assert.False(m.Entries["link/planted.md"].Deleted, "a path behind a link was tombstoned as if it had been deleted here");
            Assert.DoesNotContain(m.Entries.Keys, k => k.EndsWith("own.txt", StringComparison.Ordinal));
        }
        Assert.Equal(["own.txt"], Files(outside).Select(Path.GetFileName));
    }

    [Fact(DisplayName = "a manifest clock that is malformed, a path part that is a device name and paths that differ only by case each fail the sync with a message naming the problem", Timeout = 30000)]
    public async Task Manifest_problems_are_named()
    {
        Site a = NewSite("m1");
        await Publish(a, new() { ["w/proposal.md"] = new Hostile("x", Clock: "not-a-clock") });
        SyncReport bad = await Sync(a);
        Assert.Equal(SyncOutcome.Failed, bad.Outcome);
        Assert.Contains("clock", bad.Message);

        Site b = NewSite("m2");
        await Publish(b, new() { ["w/aux.md"] = new Hostile("x") });
        Assert.Contains("w/aux.md", (await Sync(b)).Message);

        Site c = NewSite("m3");
        await Publish(c, new() { ["w/Note.md"] = new Hostile("1"), ["w/note.md"] = new Hostile("2") });
        SyncReport clash = await Sync(c);
        Assert.Equal(SyncOutcome.Failed, clash.Outcome);
        Assert.Contains("letter case", clash.Message);
    }

    [Fact(DisplayName = "a history longer than the limit is cut to it, and a history entry that is not a hash fails the sync", Timeout = 30000)]
    public async Task History_is_cut_and_validated()
    {
        Site a = NewSite("h1");
        await Publish(a, new() { ["w/proposal.md"] = new Hostile("x", History: Enumerable.Range(0, 50).Select(i => Hash("v" + i)).ToArray()) });
        Assert.Equal(SyncOutcome.Synced, (await Sync(a)).Outcome);
        int kept = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(a.State, "state.json"))).RootElement
            .GetProperty("entries").EnumerateObject().First().Value.GetProperty("history").GetArrayLength();
        Assert.Equal(20, kept);

        Site b = NewSite("h2");
        await Publish(b, new() { ["w/proposal.md"] = new Hostile("x", History: ["../../etc"]) });
        Assert.Equal(SyncOutcome.Failed, (await Sync(b)).Outcome);
    }

    [Fact(DisplayName = "a folder storage refuses a file larger than the manifest cap instead of reading it into memory", Timeout = 30000)]
    public async Task Folder_storage_caps_the_manifest()
    {
        string folder = Path.Combine(_temp.FullName, "bigfolder");
        Directory.CreateDirectory(folder);
        await using (FileStream big = File.Create(Path.Combine(folder, "manifest.json"))) big.SetLength(17L * 1024 * 1024);
        await Assert.ThrowsAsync<BoardStorageException>(() => BoardSyncApi.FolderStorage(folder).Get("manifest.json"));
    }

    [Fact(DisplayName = "an unexpected exception inside a sync comes back as Failed, but cancellation still cancels", Timeout = 30000)]
    public async Task Unexpected_exceptions_become_failed()
    {
        Site s = new(Path.Combine(_temp.FullName, "boom"), S3(new FuncHandler(_ => throw new InvalidOperationException("boom"))));
        Directory.CreateDirectory(s.Dir);
        SyncReport r = await Sync(s);
        Assert.Equal(SyncOutcome.Failed, r.Outcome);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        Site c = new(Path.Combine(_temp.FullName, "cancel"), S3(new FuncHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)))));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BoardSyncApi.Sync(new SyncOptions(c.Root, c.State, c.Storage, "dev", () => _now), cancelled.Token));
    }

    [Fact(DisplayName = "markers that arrived through the sync never pause it, but the same markers edited by hand do", Timeout = 30000)]
    public async Task Synced_markers_do_not_pause_but_edited_ones_do()
    {
        Site s = NewSite("markers");
        string git = Path.Combine(s.Dir, ".git");
        Directory.CreateDirectory(git);
        string conflicted = "# T\n<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> branch\n";
        await Publish(s, new() { ["w/design.md"] = new Hostile(conflicted) });
        await Sync(s, git: git);
        Assert.NotEqual(SyncOutcome.Blocked, (await Sync(s, git: git)).Outcome);
        Assert.Null(BoardSyncApi.GitPause(s.Root, git, s.State));
        File.WriteAllText(Path.Combine(s.Root, "w/design.md"), conflicted + "edited by hand\n");
        SyncReport blocked = await Sync(s, git: git);
        Assert.Equal(SyncOutcome.Blocked, blocked.Outcome);
        Assert.Equal(SyncBlock.GitBusy, blocked.Blocked);
    }

    [Fact(DisplayName = "a merge that times out keeps the newer side whole and reports one conflict")]
    public void Merge_falls_back_to_the_newer_side()
    {
        string baseText = "## 1. A\n\n- [ ] 1.1 one\n";
        string local = baseText + "- [ ] 1.2 " + string.Concat(Enumerable.Repeat("<!-- agent: ", 6_000)) + "\n";
        string remote = "## 1. A\n\n- [x] 1.1 one\n";
        MergeResult result = BoardSyncApi.Merge("w/tasks.md", baseText, local, remote, remoteIsNewer: true);
        Assert.Equal(remote, result.Text);
        Assert.Single(result.Conflicts);
    }
}
