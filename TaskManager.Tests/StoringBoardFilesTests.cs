using System.Security.Cryptography;
using System.Text;
using BoardSync;
using BoardSync.PublicDTOs;
using BoardSync.Storage;

namespace TaskManager.Tests;

/// <summary>The storage contract the board sync talks to, run against both providers: a folder and an S3-compatible bucket (a fake one here, no network).</summary>
public sealed class StoringBoardFilesTests
{
    /// <summary>A provider under test, its cleanup, and a way to make it refuse everything.</summary>
    private sealed class Store : IDisposable
    {
        private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("rtm-storage-");
        public IBoardStorage Storage { get; }
        public FakeS3? Fake { get; }
        public string Folder => Path.Combine(_temp.FullName, "store");

        public Store(string provider)
        {
            if (provider == "Folder") Storage = BoardSyncApi.FolderStorage(Folder);
            else
            {
                Fake = new FakeS3();
                Storage = BoardSyncApi.S3Storage("https://account.r2.example.test", FakeS3.Bucket, "boards/main", FakeS3.AccessKey, FakeS3.SecretKey, FakeS3.Region, Fake);
            }
        }

        public void MakeItRefuse()
        {
            if (Fake is not null) Fake.RefuseEverything = true;
            else { if (Directory.Exists(Folder)) Directory.Delete(Folder, true); File.WriteAllText(Folder, "a file where the folder should be"); }
        }

        public void Dispose() { try { _temp.Delete(true); } catch (IOException) { } }
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    private static string Text(StoredFile? file) => Encoding.UTF8.GetString(file!.Bytes);

    [Theory(DisplayName = "A file put into the storage reads back with a version, and a missing file reads as not there")]
    [InlineData("Folder")]
    [InlineData("S3")]
    public async Task PutAndGet(string provider)
    {
        using Store store = new(provider);

        PutResult put = await store.Storage.Put("manifest.json", Bytes("{\"a\":1}"), PutCondition.Always);
        StoredFile? read = await store.Storage.Get("manifest.json");

        Assert.True(put.Written);
        Assert.False(string.IsNullOrEmpty(put.Version));
        Assert.Equal("{\"a\":1}", Text(read));
        Assert.Equal(put.Version, read!.Version);
        Assert.Null(await store.Storage.Get("objects/ab/nothing"));
    }

    [Theory(DisplayName = "A read with the current version answers not modified, and with an old version returns the new bytes")]
    [InlineData("Folder")]
    [InlineData("S3")]
    public async Task ReadIfChanged(string provider)
    {
        using Store store = new(provider);
        string first = (await store.Storage.Put("manifest.json", Bytes("one"), PutCondition.Always)).Version!;

        StoredFile? same = await store.Storage.GetIfChanged("manifest.json", first);
        string second = (await store.Storage.Put("manifest.json", Bytes("two"), PutCondition.Always)).Version!;
        StoredFile? changed = await store.Storage.GetIfChanged("manifest.json", first);
        StoredFile? fromNothing = await store.Storage.GetIfChanged("manifest.json", null);

        Assert.True(same!.IsNotModified);
        Assert.False(changed!.IsNotModified);
        Assert.Equal(("two", second), (Text(changed), changed.Version));
        Assert.Equal("two", Text(fromNothing));
        Assert.Null(await store.Storage.GetIfChanged("nothing.json", first));
    }

    [Theory(DisplayName = "A put that must not overwrite succeeds once and is refused the second time")]
    [InlineData("Folder")]
    [InlineData("S3")]
    public async Task OnlyIfAbsent(string provider)
    {
        using Store store = new(provider);

        PutResult first = await store.Storage.Put("objects/ab/abc", Bytes("first"), PutCondition.OnlyIfAbsent);
        PutResult second = await store.Storage.Put("objects/ab/abc", Bytes("second"), PutCondition.OnlyIfAbsent);

        Assert.True(first.Written);
        Assert.False(second.Written);
        Assert.Equal("first", Text(await store.Storage.Get("objects/ab/abc")));
    }

    [Theory(DisplayName = "A put for a version that is no longer current is refused and leaves the newer content")]
    [InlineData("Folder")]
    [InlineData("S3")]
    public async Task OnlyIfVersion(string provider)
    {
        using Store store = new(provider);
        string old = (await store.Storage.Put("manifest.json", Bytes("v1"), PutCondition.Always)).Version!;
        string current = (await store.Storage.Put("manifest.json", Bytes("v2"), PutCondition.Always)).Version!;

        PutResult stale = await store.Storage.Put("manifest.json", Bytes("from a stale reader"), PutCondition.OnlyIfVersion(old));
        PutResult missing = await store.Storage.Put("other.json", Bytes("x"), PutCondition.OnlyIfVersion(old));
        PutResult fresh = await store.Storage.Put("manifest.json", Bytes("v3"), PutCondition.OnlyIfVersion(current));

        Assert.False(stale.Written);
        Assert.False(missing.Written);
        Assert.Null(await store.Storage.Get("other.json"));
        Assert.True(fresh.Written);
        StoredFile? after = await store.Storage.Get("manifest.json");
        Assert.Equal(("v3", fresh.Version), (Text(after), after!.Version));
        Assert.True(store.Storage.ConditionalWrites);
    }

    [Theory(DisplayName = "Listing a prefix returns every path under it with its version, however many there are")]
    [InlineData("Folder")]
    [InlineData("S3")]
    public async Task ListPrefix(string provider)
    {
        using Store store = new(provider);
        Dictionary<string, string> expected = [];
        for (int i = 0; i < 7; i++)
        {
            string path = $"objects/{i:x2}/{i:x2}file{i}";
            expected[path] = (await store.Storage.Put(path, Bytes("content " + i), PutCondition.OnlyIfAbsent)).Version!;
        }
        await store.Storage.Put("manifest.json", Bytes("m"), PutCondition.Always);
        await store.Storage.Put("objectsx/not-under-it", Bytes("n"), PutCondition.Always);

        IReadOnlyList<StoredEntry> objects = await store.Storage.List("objects/");
        IReadOnlyList<StoredEntry> everything = await store.Storage.List("");
        IReadOnlyList<StoredEntry> none = await store.Storage.List("nothing/");

        Assert.Equal(expected.OrderBy(e => e.Key, StringComparer.Ordinal), objects.OrderBy(e => e.Path, StringComparer.Ordinal).Select(e => KeyValuePair.Create(e.Path, e.Version)));
        Assert.Equal(9, everything.Count);
        Assert.Contains(everything, e => e.Path == "manifest.json");
        Assert.Empty(none);
        if (store.Fake is not null) Assert.True(store.Fake.Requests > 7 + 2 + 3, "the listing should have followed the pages");
    }

    [Theory(DisplayName = "Deleting removes the file, and deleting what is not there is not an error")]
    [InlineData("Folder")]
    [InlineData("S3")]
    public async Task Delete(string provider)
    {
        using Store store = new(provider);
        await store.Storage.Put("objects/ab/abc", Bytes("x"), PutCondition.Always);

        await store.Storage.Delete("objects/ab/abc");
        await store.Storage.Delete("objects/ab/abc");
        await store.Storage.Delete("never/existed");

        Assert.Null(await store.Storage.Get("objects/ab/abc"));
        Assert.Empty(await store.Storage.List(""));
    }

    [Theory(DisplayName = "Paths that climb out of the storage are refused")]
    [InlineData("Folder")]
    [InlineData("S3")]
    public async Task ClimbingPaths(string provider)
    {
        using Store store = new(provider);

        foreach (string path in new[] { "../outside.txt", "objects/../../outside.txt", "/absolute.txt", "C:/drive.txt", "back\\slash.txt", "", "a//b" })
        {
            await Assert.ThrowsAnyAsync<ArgumentException>(() => store.Storage.Get(path));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => store.Storage.Put(path, Bytes("x"), PutCondition.Always));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => store.Storage.Delete(path));
        }
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.Storage.List("../"));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(store.Folder)!, "outside.txt")));
    }

    [Theory(DisplayName = "Test connection writes, reads back and removes a probe, and says what failed when the storage refuses")]
    [InlineData("Folder")]
    [InlineData("S3")]
    public async Task TestConnection(string provider)
    {
        using Store store = new(provider);

        (bool ok, string message) = await BoardSyncApi.TestConnection(store.Storage);
        Assert.True(ok, message);
        Assert.Empty(await store.Storage.List(""));   // the probe is gone

        store.MakeItRefuse();
        (bool refusedOk, string refusedMessage) = await BoardSyncApi.TestConnection(store.Storage);

        Assert.False(refusedOk);
        Assert.False(string.IsNullOrWhiteSpace(refusedMessage));
        Assert.Contains(provider == "S3" ? "refused" : "folder", refusedMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "The S3 signature matches the published AWS Signature Version 4 example")]
    public void SignatureExample()
    {
        // GET Object example from the AWS documentation ("Signature Calculations for the Authorization Header").
        string authorization = S3Signer.Authorization(
            "GET", "/test.txt", "",
            new Dictionary<string, string> { ["host"] = "examplebucket.s3.amazonaws.com", ["range"] = "bytes=0-9" },
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            new DateTimeOffset(2013, 5, 24, 0, 0, 0, TimeSpan.Zero), "us-east-1",
            "AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY");

        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20130524/us-east-1/s3/aws4_request,SignedHeaders=host;range;x-amz-content-sha256;x-amz-date,Signature=f0e8bdb87c964420e857bd35b5d6ed310bd44f0170aba48dd91039c6036bdb41",
            authorization);
    }

    [Fact(DisplayName = "A refused key is reported as an error a person can act on, not as a missing file")]
    public async Task RefusedKey()
    {
        using Store store = new("S3");
        store.Fake!.RefuseEverything = true;

        BoardStorageException read = await Assert.ThrowsAsync<BoardStorageException>(() => store.Storage.Get("manifest.json"));
        BoardStorageException write = await Assert.ThrowsAsync<BoardStorageException>(() => store.Storage.Put("manifest.json", Bytes("x"), PutCondition.Always));
        BoardStorageException list = await Assert.ThrowsAsync<BoardStorageException>(() => store.Storage.List(""));

        foreach (BoardStorageException error in new[] { read, write, list })
        {
            Assert.Contains("403", error.Message);
            Assert.Contains(FakeS3.AccessKey[^4..], error.Message);      // only the end of the key id is shown
            Assert.DoesNotContain(FakeS3.AccessKey, error.Message);
            Assert.DoesNotContain(FakeS3.SecretKey, error.Message);
        }
    }

    [Fact(DisplayName = "S3 objects live under the bucket and prefix, and names with spaces and non-ASCII letters are signed as sent")]
    public async Task PrefixAndEncoding()
    {
        using Store store = new("S3");

        await store.Storage.Put("notes/my note (1) é+&=.md", Bytes("x"), PutCondition.Always);
        IReadOnlyList<StoredEntry> listed = await store.Storage.List("notes/my note");

        Assert.Equal([FakeS3.Bucket + "/boards/main/notes/my note (1) é+&=.md"], store.Fake!.Objects.Keys);
        Assert.Equal(["notes/my note (1) é+&=.md"], listed.Select(e => e.Path));
        Assert.Equal("x", Text(await store.Storage.Get("notes/my note (1) é+&=.md")));
    }

    [Fact(DisplayName = "Two writers racing on a conditional put into a folder: exactly one wins")]
    public async Task RacingWriters()
    {
        using Store store = new("Folder");
        string version = (await store.Storage.Put("manifest.json", Bytes("start"), PutCondition.Always)).Version!;

        PutResult[] results = await Task.WhenAll(Enumerable.Range(0, 24).Select(i =>
            Task.Run(() => store.Storage.Put("manifest.json", Bytes("writer " + i), PutCondition.OnlyIfVersion(version)))));

        PutResult winner = Assert.Single(results, r => r.Written);
        StoredFile? after = await store.Storage.Get("manifest.json");
        Assert.StartsWith("writer ", Text(after));
        Assert.Equal(winner.Version, after!.Version);
        Assert.Equal(23, results.Count(r => !r.Written));
        Assert.Single(await store.Storage.List(""));   // no lock or temp files listed
    }
    // ---- s3-stream chunk ----

    /// <summary>Replaces the body of every successful GET with a generated one of <see cref="Length"/> bytes, read in whatever sizes the caller asks for.</summary>
    private sealed class StreamedBodyHandler(FakeS3 inner, long length, bool announce) : DelegatingHandler(inner)
    {
        public long Length => length;
        public int LargestRead { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            HttpResponseMessage response = await base.SendAsync(request, ct);
            if (request.Method != HttpMethod.Get || !response.IsSuccessStatusCode) return response;
            StreamContent content = new(new Generated(this, length, seekable: announce));
            if (announce) content.Headers.ContentLength = length;
            response.Content = content;
            return response;
        }

        public static byte At(long position) => (byte)(position * 31 + (position >> 8));

        private sealed class Generated(StreamedBodyHandler owner, long length, bool seekable) : Stream
        {
            private long _position;
            public override bool CanRead => true;
            public override bool CanSeek => seekable;
            public override bool CanWrite => false;
            public override long Length => seekable ? length : throw new NotSupportedException();
            public override long Position { get => _position; set { if (value != _position) throw new NotSupportedException(); } }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => Fill(buffer.AsSpan(offset, count));
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => new(Fill(buffer.Span));
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            private int Fill(Span<byte> span)
            {
                owner.LargestRead = Math.Max(owner.LargestRead, span.Length);
                int n = (int)Math.Min(span.Length, length - _position);
                for (int i = 0; i < n; i++) span[i] = At(_position + i);
                _position += n;
                return n;
            }
        }
    }

    /// <summary>A destination that keeps only a hash, a count and the largest single write.</summary>
    private sealed class HashingSink : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        public long Written { get; private set; }
        public int LargestWrite { get; private set; }
        public string Hash => Convert.ToHexString(_hash.GetHashAndReset());
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Written;
        public override long Position { get => Written; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) { _hash.AppendData(buffer); Written += buffer.Length; LargestWrite = Math.Max(LargestWrite, buffer.Length); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) { Write(buffer.Span); return ValueTask.CompletedTask; }
    }

    private static IBoardStorage StreamingStorage(StreamedBodyHandler handler) =>
        BoardSyncApi.S3Storage("https://account.r2.example.test", FakeS3.Bucket, "boards/main", FakeS3.AccessKey, FakeS3.SecretKey, FakeS3.Region, handler);

    [Fact(DisplayName = "An S3 object is streamed to its destination without being held whole in memory")]
    public async Task S3ObjectIsStreamed()
    {
        FakeS3 fake = new();
        StreamedBodyHandler handler = new(fake, 12L * 1024 * 1024 + 5, announce: true);
        IBoardStorage storage = StreamingStorage(handler);
        await storage.Put("objects/ab/abcdef", Bytes("small stand-in"), PutCondition.Always);
        HashingSink sink = new();

        Assert.True(await storage.GetTo("objects/ab/abcdef", sink));

        using IncrementalHash expected = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] block = new byte[65536];
        for (long at = 0; at < handler.Length; at += block.Length)
        {
            int n = (int)Math.Min(block.Length, handler.Length - at);
            for (int i = 0; i < n; i++) block[i] = StreamedBodyHandler.At(at + i);
            expected.AppendData(block, 0, n);
        }
        Assert.Equal(handler.Length, sink.Written);
        Assert.Equal(Convert.ToHexString(expected.GetHashAndReset()), sink.Hash);
        Assert.InRange(sink.LargestWrite, 1, 81920);          // never one buffer of the whole body
        Assert.InRange(handler.LargestRead, 1, 81920);
        Assert.False(await storage.GetTo("objects/ab/missing", new HashingSink()));
        Assert.Equal(2 + 1, fake.Requests);                    // put, get, missing get: no extra round trips
    }

    [Theory(DisplayName = "A streamed S3 object over the cap is refused and nothing is left at the destination")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task S3StreamedObjectOverTheCap(bool announced)
    {
        FakeS3 fake = new();
        StreamedBodyHandler handler = new(fake, 3L * 1024 * 1024, announce: announced);
        IBoardStorage storage = StreamingStorage(handler);
        await storage.Put("objects/ab/abcdef", Bytes("small stand-in"), PutCondition.Always);
        HashingSink sink = new();

        using (StorageLimits.OverrideObjectMax(1024 * 1024))
        {
            BoardStorageException e = await Assert.ThrowsAsync<BoardStorageException>(() => storage.GetTo("objects/ab/abcdef", sink));
            Assert.True(e.IsTooLarge);
        }

        if (announced) Assert.Equal(0, sink.Written);          // refused on the announced length, before a byte is written
        else Assert.True(sink.Written <= 1024 * 1024);          // never past the cap
    }
}
