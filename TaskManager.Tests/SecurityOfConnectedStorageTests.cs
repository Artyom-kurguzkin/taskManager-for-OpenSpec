using System.Diagnostics;
using System.Net;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using BoardSync;
using BoardSync.PublicDTOs;
using Bunit;
using GitHistory;
using Microsoft.Extensions.DependencyInjection;
using Notes;
using Notes.PublicDTOs;
using OpenSpecStore;
using Radzen;
using TaskManager.Components;
using TaskManager.Components.Pages;
using TaskManager.Services;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Tests;

/// <summary>
/// A security review of connected storage as an attacker would meet it. Each test states the SAFE behaviour; a test that fails today proves a defect, a test that
/// passes proves that point is safe. Attackers: a repository the user clones (it controls openspec/config.yaml and every board file), someone with a copy of the
/// repository or of the profile, another local process, whoever reads logs, toasts or the board, and a hostile storage. Temp folders and fake handlers only: no
/// real network, no UNC path is ever opened.
/// </summary>
[Collection(nameof(ModuleStateCollection))]
public sealed class SecurityOfConnectedStorageTests : IDisposable
{
    private const string Item = "openspec/changes/add-health-check";
    private const string Secret = FakeS3.SecretKey;
    private const string KeyId = FakeS3.AccessKey;

    private readonly string temp = Directory.CreateTempSubdirectory("rtm-sec-").FullName;
    private readonly List<IDisposable> disposables = [];
    private readonly List<string> junctions = [];
    private readonly List<string> cleanup = [];
    private string Storage => Path.Combine(temp, "storage");

    public void Dispose()
    {
        foreach (IDisposable d in disposables) d.Dispose();
        foreach (string j in junctions) try { Directory.Delete(j); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        foreach (string c in cleanup) try { if (Directory.Exists(c)) Directory.Delete(c, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        try { Directory.Delete(temp, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ── helpers ──

    /// <summary>Records every request that would leave the machine, then lets the fake S3 answer it.</summary>
    private sealed class Recorder(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public readonly List<(Uri Uri, string? Authorization, string Method, int Body)> Requests = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int body = request.Content is null ? 0 : (int)(request.Content.Headers.ContentLength ?? 0);
            lock (Requests) Requests.Add((request.RequestUri!, request.Headers.TryGetValues("Authorization", out IEnumerable<string>? a) ? string.Join(",", a) : null, request.Method.Method, body));
            return await base.SendAsync(request, cancellationToken);
        }

        public (Uri Uri, string? Authorization, string Method, int Body)[] Snapshot() { lock (Requests) return [.. Requests]; }
        public void Clear() { lock (Requests) Requests.Clear(); }
    }

    private sealed class Canned(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(answer(request));
    }

    /// <summary>The built-in S3 provider, but opened over a recording fake network instead of the real one.</summary>
    private static StorageProvider S3Over(HttpMessageHandler handler) => new("s3", StorageProviders.S3.Name, StorageProviders.S3.Hint, StorageProviders.S3.Fields,
        v => BoardSyncApi.S3Storage(v["endpoint"].Trim(), v["bucket"].Trim(), v.GetValueOrDefault("prefix"), v["accessKey"].Trim(), v["secretKey"].Trim(), "auto", handler));

    private sealed class Box : IDisposable
    {
        public readonly TestRepo Repo = new();
        public readonly string Data;
        public readonly UiService Ui = new();
        public readonly BoardStorageSettings Settings;
        public readonly BoardSyncService Sync;
        public readonly List<SyncOptions> Seen = [];
        public Func<SyncOptions, Task<SyncReport>>? Engine;

        public Box(string temp, string name, IReadOnlyList<StorageProvider>? providers = null)
        {
            Data = Path.Combine(temp, "data-" + name);
            OpenSpecStoreApi.Open(Repo.Root, keepOthers: true);
            Settings = new BoardStorageSettings(Data, providers ?? StorageProviders.Builtin);
            Sync = new BoardSyncService(Settings, Ui, new SyncSetup(TimeSpan.FromMinutes(2),
                async o => { lock (Seen) Seen.Add(o); return Engine is null ? await BoardSyncApi.Sync(o) : await Engine(o); }));
            Sync.InProject = In;
        }

        public string Root => Repo.Root;
        public string ConfigFile => Repo.PathOf("openspec/config.yaml");
        public void Attach() => _ = Sync.Attach(Root);
        /// <summary>The pass the service started last: awaiting it is how a test waits for a sync.</summary>
        public Task Pass() => Sync.LastPass(Root);
        public IReadOnlyList<string> Toasts => Ui.Toasts.Select(t => t.Text).ToList();
        public SyncOptions[] SeenNow() { lock (Seen) return [.. Seen]; }

        public static void In(string root, Action work)
        {
            OpenSpecStoreApi.Use(root);
            GitHistoryApi.Configure(root);
            work();
        }

        public void Do(Action work) => In(Root, work);

        public T Do<T>(Func<T> work)
        {
            T result = default!;
            In(Root, () => result = work());
            return result;
        }

        /// <summary>What a repository's own config.yaml can say, exactly as a clone would bring it: the lines go under x-rtm: boardStorage:.</summary>
        public void RepoSays(params string[] boardStorageLines) =>
            Repo.Write("openspec/config.yaml", "schema: spec-driven\n\nx-rtm:\n  lanes:\n    In Progress: [High priority, Lower priority]\n  boardStorage:\n" + string.Concat(boardStorageLines.Select(l => "    " + l + "\n")));

        public void UserConnectsFolder(string folder)
        {
            Assert.True(Settings.Connect(Root, "folder").Success);
            Assert.True(Settings.SetValue(Root, "path", folder).Success);
        }

        public void UserConnectsS3(string endpoint, string bucket = FakeS3.Bucket, string key = KeyId, string secret = Secret)
        {
            Assert.True(Settings.Connect(Root, "s3").Success);
            Assert.True(Settings.SetValue(Root, "endpoint", endpoint).Success);
            Assert.True(Settings.SetValue(Root, "bucket", bucket).Success);
            Assert.True(Settings.SetValue(Root, "accessKey", key).Success);
            Assert.True(Settings.SetValue(Root, "secretKey", secret).Success);
        }

        public void Dispose()
        {
            Sync.Dispose();
            Repo.Dispose();
        }
    }

    private Box NewBox(string name, IReadOnlyList<StorageProvider>? providers = null)
    {
        Box b = new(temp, name, providers);
        disposables.Add(b);
        return b;
    }

    private static string Q(string s) => "'" + s.Replace("'", "''") + "'";

    private static void Junction(string link, string target)
    {
        ProcessStartInfo info = new("cmd.exe") { CreateNoWindow = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (string a in new[] { "/c", "mklink", "/J", link, target }) info.ArgumentList.Add(a);
        using Process p = Process.Start(info)!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(Directory.Exists(link), "could not create a junction for the test");
    }

    private static IEnumerable<string> AllFiles(string folder) => Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories) : [];

    // ═══ 1. settings that come from the repository ═══

    [Fact(DisplayName = "A repository's own settings cannot start syncing the board to a folder the user never chose")]
    public async Task RepositoryCannotChooseTheFolder()
    {
        Box a = NewBox("a");
        string attacker = Path.Combine(temp, "attacker-share");
        a.RepoSays("provider: folder", "path: " + Q(attacker));    // what a cloned config.yaml says; the user did nothing

        a.Attach();                                                  // opening the project
        await a.Pass();

        Assert.False(File.Exists(Path.Combine(attacker, "manifest.json")), "the whole board was uploaded to " + attacker + " on open, with no question asked");
        Assert.Equal(0, a.Sync.Runs(a.Root));
    }

    [Fact(DisplayName = "A repository's settings cannot point the sync at a network share (UNC path) without the user's say")]
    public void RepositoryCannotChooseAUncShare()
    {
        Box a = NewBox("a");
        a.RepoSays("provider: folder", @"path: '\\attacker.example\share\board'");    // opening it makes Windows send the NTLM hash and the board goes there

        (BoardSync.PublicDTOs.IBoardStorage? storage, string? problem) = a.Settings.Open(a.Root);     // building the storage touches no network: only the path is read
        (storage as IDisposable)?.Dispose();

        Assert.Null(storage);
        Assert.False(string.IsNullOrEmpty(problem));
    }

    [Fact(DisplayName = "A relative folder path is not resolved against the app's working directory")]
    public async Task RelativeFolderIsNotRelativeToTheProcess()
    {
        string rel = "rtm-sec-rel-" + Guid.NewGuid().ToString("N");
        string landed = Path.Combine(Environment.CurrentDirectory, rel);
        cleanup.Add(landed);
        Box a = NewBox("a");
        a.UserConnectsFolder(rel);                                    // the user (or a repository) gives a relative path

        a.Attach();
        await a.Pass();

        Assert.False(Directory.Exists(landed), "the board was written under the process's working directory: " + landed);
    }

    [Fact(DisplayName = "Keys saved for one S3 address are not sent to another address after the repository's settings change")]
    public async Task SavedKeysAreBoundToTheirAddress()
    {
        Recorder rec = new(new FakeS3());
        Box a = NewBox("a", [S3Over(rec), StorageProviders.Folder]);
        a.UserConnectsS3("https://storage.legit.example");
        a.Attach();
        await a.Pass();
        Assert.NotEmpty(rec.Snapshot());
        Assert.All(rec.Snapshot(), r => Assert.Equal("storage.legit.example", r.Uri.Host));      // sanity: the address the user chose

        string config = File.ReadAllText(a.ConfigFile);                                            // a git pull changes the endpoint
        File.WriteAllText(a.ConfigFile, config.Replace("storage.legit.example", "storage.attacker.example"));
        Assert.Contains("storage.attacker.example", File.ReadAllText(a.ConfigFile));
        rec.Clear();
        await a.Sync.SyncNow(a.Root);

        (Uri Uri, string? Authorization, string Method, int Body)[] toAttacker = [.. rec.Snapshot().Where(r => r.Uri.Host == "storage.attacker.example")];
        Assert.True(toAttacker.Length == 0,
            toAttacker.Length == 0 ? "" : $"{toAttacker.Length} signed request(s) went to the attacker's host, e.g. {toAttacker[0].Method} {toAttacker[0].Uri.AbsolutePath} with {toAttacker[0].Authorization?[..Math.Min(70, toAttacker[0].Authorization?.Length ?? 0)]}");
    }

    [Fact(DisplayName = "A changed S3 address is not used until the user confirms it, and the board is not uploaded meanwhile")]
    public async Task ChangedAddressNeedsConfirmation()
    {
        FakeS3 fake = new();
        Recorder rec = new(fake);
        Box a = NewBox("a", [S3Over(rec), StorageProviders.Folder]);
        a.UserConnectsS3("https://storage.legit.example");
        a.Attach();
        await a.Pass();
        int before = fake.Objects.Count;
        Assert.True(before > 0);

        File.WriteAllText(a.ConfigFile, File.ReadAllText(a.ConfigFile).Replace("storage.legit.example", "storage.attacker.example"));
        a.Repo.Write(Item + "/proposal.md", "A board change made after the address changed underneath.\n");
        rec.Clear();
        await a.Sync.SyncNow(a.Root);

        Assert.DoesNotContain(rec.Snapshot(), r => r.Uri.Host == "storage.attacker.example" && r.Method is "PUT" or "DELETE");
        Assert.Contains(a.Toasts.Concat([a.Sync.Status(a.Root).Line]), t => t.Contains("address", StringComparison.OrdinalIgnoreCase) || t.Contains("confirm", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "An S3 endpoint over plain http to a remote host is refused")]
    public void PlainHttpEndpointIsRefused()
    {
        Box a = NewBox("a");
        a.UserConnectsS3("http://storage.attacker.example");

        (BoardSync.PublicDTOs.IBoardStorage? storage, string? problem) = a.Settings.Open(a.Root);
        (storage as IDisposable)?.Dispose();

        Assert.True(storage is null, "the board and the access key id would be sent unencrypted (and the signed request can be replayed for 15 minutes)");
        Assert.False(string.IsNullOrEmpty(problem));
    }

    [Fact(DisplayName = "A hostile board-storage section in config.yaml never throws when read")]
    public async Task HostileConfigNeverThrows()
    {
        string[][] cases =
        [
            ["provider:"],                                         // an empty value is null
            ["provider: [a, b]"],
            ["provider: {a: b}"],
            ["provider: s3", "provider: folder"],                  // a duplicate key
            ["provider: folder", "path:"],
            ["provider: &p folder", "path: *p"],
            ["provider: !!binary Zm9sZGVy"],
            ["- provider", "- folder"],
            ["provider: folder", "path: [" + string.Join(",", Enumerable.Repeat("[", 200)) + "]"],
            ["provider: \"" + new string('x', 200_000) + "\""],
        ];
        List<string> threw = [];
        foreach (string[] lines in cases)
        {
            Box a = NewBox("h" + threw.Count + "-" + Guid.NewGuid().ToString("N")[..4]);
            a.RepoSays(lines);
            try
            {
                a.Settings.Config(a.Root);
                a.Settings.Problem(a.Root);
                (a.Settings.Open(a.Root).Storage as IDisposable)?.Dispose();
                a.Attach();
                await a.Pass();
            }
            catch (Exception e) { threw.Add($"{string.Join(" / ", lines.Select(l => l.Length > 40 ? l[..40] + "..." : l))} -> {e.GetType().Name}"); }
        }
        Assert.True(threw.Count == 0, "opening a repository with this config.yaml threw: " + string.Join("; ", threw));
    }

    [Fact(DisplayName = "A project whose config.yaml has an empty provider still opens")]
    public void EmptyProviderDoesNotBreakOpeningTheProject()
    {
        Box a = NewBox("a");
        a.RepoSays("provider:");
        using Workspace workspace = new(new SettingsStore(Path.Combine(temp, "settings-open.json")), a.Ui, a.Sync);

        (bool success, string message) = workspace.Open(a.Root);

        Assert.True(success, message);
    }

    [Fact(DisplayName = "A storage folder inside the board itself is refused instead of syncing the board into itself")]
    public async Task StorageInsideTheBoardIsRefused()
    {
        Box a = NewBox("a");
        string board = a.Repo.PathOf("openspec/changes");
        a.UserConnectsFolder(Path.Combine(board, "store"));

        a.Attach();
        await a.Pass();

        Assert.False(Directory.Exists(Path.Combine(board, "store")), "the sync wrote its own manifest and objects into the board it is syncing, and would upload them next");
    }

    [Fact(DisplayName = "A saved address value cannot carry extra settings in, and the provider cannot be set as a value")]
    public void SettingValuesCannotInjectYaml()
    {
        Box a = NewBox("a");
        a.UserConnectsFolder(Storage);
        string nasty = "x\n  provider: s3\n  bucket: evil\nevil: 1 # c";

        Assert.True(a.Settings.SetValue(a.Root, "path", nasty).Success);
        Assert.False(a.Settings.SetValue(a.Root, "provider", "s3").Success);

        BoardStorageConfig config = a.Settings.Config(a.Root)!;
        Assert.Equal("folder", config.Provider);
        Assert.Equal(nasty.Trim(), config.Values["path"]);
        Assert.False(config.Values.ContainsKey("bucket"));
        Assert.False(config.Values.ContainsKey("evil"));
    }

    // ═══ 2. the secret at rest ═══

    [Fact(DisplayName = "The secret is stored with Windows per-user protection and appears nowhere in plain or base64 form, even after a sync")]
    public async Task SecretIsProtectedAtRest()
    {
        FakeS3 fake = new();
        Box a = NewBox("a", [S3Over(fake), StorageProviders.Folder]);
        a.UserConnectsS3("https://storage.legit.example");
        a.Attach();
        await a.Pass();
        await a.Sync.SyncNow(a.Root);

        string credentials = Path.Combine(a.Settings.ProjectFolder(a.Root), "credentials.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(credentials));
        string stored = doc.RootElement.GetProperty("Protected").GetProperty("secretKey").GetString()!;
        if (OperatingSystem.IsWindows()) Assert.StartsWith("dpapi:", stored);
        Assert.False(doc.RootElement.GetProperty("Plain").TryGetProperty("secretKey", out _));

        string[] needles = [Secret, Convert.ToBase64String(Encoding.UTF8.GetBytes(Secret)), Convert.ToHexString(Encoding.UTF8.GetBytes(Secret))];
        foreach (string folder in new[] { a.Data, a.Root, temp })
            foreach (string file in AllFiles(folder).Where(f => !f.EndsWith(".exe") && new System.IO.FileInfo(f).Length < 5_000_000))
            {
                string text;
                try { text = Encoding.Latin1.GetString(File.ReadAllBytes(file)); } catch (IOException) { continue; }
                foreach (string needle in needles) Assert.False(text.Contains(needle, StringComparison.OrdinalIgnoreCase), $"the secret is in {file}");
            }
        Assert.Empty(AllFiles(a.Data).Where(f => f.EndsWith(".tmp")));
    }

    [Fact(DisplayName = "The saved credentials file and the app-data folder are readable by this user only")]
    public void CredentialsAreNotReadableByOthers()
    {
        Box a = NewBox("a");
        a.UserConnectsS3("https://storage.legit.example");
        string file = Path.Combine(a.Settings.ProjectFolder(a.Root), "credentials.json");
        Assert.True(File.Exists(file));
        if (!OperatingSystem.IsWindows()) return;

        SecurityIdentifier[] everyone = [new(WellKnownSidType.WorldSid, null), new(WellKnownSidType.BuiltinUsersSid, null), new(WellKnownSidType.AuthenticatedUserSid, null)];
        foreach (string path in new[] { file, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })    // the real default location is only read, never written
        {
            FileSystemSecurity acl = File.Exists(path) ? new System.IO.FileInfo(path).GetAccessControl() : new DirectoryInfo(path).GetAccessControl();
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & (FileSystemRights.ReadData | FileSystemRights.ReadAndExecute)) != 0)
                    Assert.DoesNotContain(rule.IdentityReference, everyone);
        }
    }

    [Fact(DisplayName = "The access key id is not stored as plain text on this computer")]
    public void AccessKeyIdIsNotPlain()
    {
        Box a = NewBox("a");
        a.UserConnectsS3("https://storage.legit.example");

        string text = File.ReadAllText(Path.Combine(a.Settings.ProjectFolder(a.Root), "credentials.json"));

        Assert.DoesNotContain(KeyId, text);
    }

    [Fact(DisplayName = "Going back to this folder only removes the saved secret from this computer")]
    public void DisconnectingForgetsTheSecret()
    {
        Box a = NewBox("a");
        a.UserConnectsS3("https://storage.legit.example");

        Assert.True(a.Settings.Disconnect(a.Root).Success);

        BoardStorageSettings fresh = new(a.Data, StorageProviders.Builtin);
        Assert.Null(fresh.ComputerValue(a.Root, "secretKey"));
        Assert.DoesNotContain(AllFiles(a.Data), f => File.ReadAllText(f).Contains("dpapi:") || File.ReadAllText(f).Contains("file:"));
    }

    [Fact(DisplayName = "Changing the storage provider removes the secret kept for the previous one")]
    public void ChangingProviderForgetsTheSecret()
    {
        Box a = NewBox("a");
        a.UserConnectsS3("https://storage.legit.example");

        Assert.True(a.Settings.Connect(a.Root, "folder").Success);

        Assert.Null(new BoardStorageSettings(a.Data, StorageProviders.Builtin).ComputerValue(a.Root, "secretKey"));
    }

    [Fact(DisplayName = "A project's sync folder is always directly inside the app's sync folder, whatever the repository is called")]
    public void ProjectFolderCannotEscape()
    {
        BoardStorageSettings settings = new(Path.Combine(temp, "data"), StorageProviders.Builtin);
        string[] roots =
        [
            @"C:\work\..", @"C:\work\.", @"C:\work\a b&c", @"C:\work\con", @"C:\work\a:b", @"C:\work\..\..\evil", @"C:\", @"\\srv\share\..", @"C:\work\" + new string('x', 150),
            "C:/work/sub/../../..", @"C:\work\%APPDATA%", @"C:\work\a\0b",
        ];
        string sync = Path.Combine(temp, "data", "sync");
        foreach (string root in roots)
        {
            string folder = settings.ProjectFolder(root);
            Assert.Equal(sync, Path.GetDirectoryName(folder));
            Assert.Matches("^[A-Za-z0-9_-]*-[0-9a-f]{8}$", Path.GetFileName(folder));
            Assert.StartsWith(sync + Path.DirectorySeparatorChar, Path.GetFullPath(settings.StateFolder(root)));
        }
        Assert.NotEqual(settings.ProjectFolder(@"C:\work\a"), settings.ProjectFolder(@"C:\other\a"));
    }

    // ═══ 3 + 4. the secret in the page, and leaks through messages ═══

    private BunitContext PageContext(Box c)
    {
        BunitContext ctx = new();
        disposables.Add(ctx);
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(c.Ui);
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(Path.Combine(temp, "settings-" + Guid.NewGuid().ToString("N") + ".json")));
        ctx.Services.AddSingleton(c.Settings);
        ctx.Services.AddSingleton(c.Sync);
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(c.Root).Success);
        return ctx;
    }

    [Fact(DisplayName = "A secret typed into the settings page is never rendered back, kept in the markup or shown in a toast")]
    public async Task SecretIsNeverRenderedBack()
    {
        Box a = NewBox("a", [S3Over(new FakeS3()), StorageProviders.Folder]);
        Assert.True(a.Settings.Connect(a.Root, "s3").Success);
        await using BunitContext ctx = PageContext(a);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();
        AngleSharp.Dom.IElement Section() => page.Find("section[data-storage]");

        await page.InvokeAsync(() => Section().QuerySelector("input[data-field='secretKey']")!.Change(Secret));

        page.WaitForAssertion(() => Assert.True(a.Settings.IsSet(a.Root, "secretKey")));
        AngleSharp.Dom.IElement input = Section().QuerySelector("input[data-field='secretKey']")!;
        Assert.Equal("password", input.GetAttribute("type"));
        Assert.True(string.IsNullOrEmpty(input.GetAttribute("value")), "the secret input has a value attribute");
        Assert.DoesNotContain(Secret, page.Markup);
        Assert.DoesNotContain(a.Toasts, t => t.Contains(Secret));
        Assert.DoesNotContain(Secret, a.Sync.Status(a.Root).Line);
    }

    private async Task<(Box Box, List<string> Messages)> FailingStorage()
    {
        FakeS3 fake = new() { RefuseEverything = true };
        Recorder rec = new(fake);
        Box a = NewBox("a", [S3Over(rec), StorageProviders.Folder]);
        a.UserConnectsS3("https://storage.legit.example");
        a.Attach();
        await a.Pass();
        SyncReport? report = await a.Sync.SyncNow(a.Root);
        (bool _, string test) = await a.Sync.Test(a.Root);
        List<string> messages = [report?.Message ?? "", test, a.Sync.Status(a.Root).Line, .. a.Toasts];
        Assert.Equal(SyncOutcome.Failed, report?.Outcome);
        return (a, messages);
    }

    [Fact(DisplayName = "A failed sync or connection test never shows the secret, the Authorization header or a signature")]
    public async Task FailureMessagesHideTheSecret()
    {
        (_, List<string> messages) = await FailingStorage();

        foreach (string m in messages)
        {
            Assert.DoesNotContain(Secret, m);
            Assert.DoesNotContain("Authorization", m, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Signature=", m, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("AWS4-HMAC", m);
        }
        Assert.Contains(messages, m => m.Contains("refused"));    // the message exists: it is just clean
    }

    [Fact(DisplayName = "A failed sync does not print the access key id in toasts, the state line or the connection test")]
    public async Task FailureMessagesHideTheAccessKeyId()
    {
        (_, List<string> messages) = await FailingStorage();

        Assert.DoesNotContain(messages, m => m.Contains(KeyId));
    }

    [Fact(DisplayName = "Text and names that arrive from the storage in a conflict cannot forge events, note headers or markup")]
    public async Task HostileConflictTextStaysInertOnTheBoard()
    {
        Box a = NewBox("a");
        a.UserConnectsFolder(Storage);
        a.Attach();
        await a.Pass();
        string hostile = "```\n</script><script>alert(1)</script>\n[x](javascript:alert(1))\n![](https://attacker.example/p.png)\n```\n---\nid: 999\nby: attacker\n";
        string forged = "2026-01-01T00:00:00 | Claude | status | Completed";
        string events = Item + "/events.log";
        int linesBefore = File.Exists(a.Repo.PathOf(events)) ? File.ReadAllLines(a.Repo.PathOf(events)).Length : 0;
        a.Engine = _ => Task.FromResult(new SyncReport(SyncOutcome.Synced, null, [], [], [], [], [],
        [
            new SyncConflict("add-health-check/.openspec.yaml", "status\n" + forged, "In Review\n" + forged, "Blocked\n" + forged),
            new SyncConflict("add-health-check/proposal.md", "description\n---\nid: 999\nby: attacker", "kept", hostile),
        ], []));

        await a.Sync.SyncNow(a.Root);
        a.Do(OpenSpecStoreApi.Refresh);

        string[] lines = File.ReadAllLines(a.Repo.PathOf(events));
        Assert.Equal(linesBefore + 1, lines.Length);                                   // one event line, however many newlines the text had
        Assert.DoesNotContain(lines, l => l.StartsWith("2026-01-01T00:00:00 | Claude | status"));
        Note[] notes = a.Do(() => NotesApi.GetForWorkItem(123).Where(n => n.Title.StartsWith("Sync conflict")).ToArray());
        Note note = Assert.Single(notes);
        Assert.Equal("Sync", note.By);
        Assert.NotEqual(999, note.Id);
        Assert.DoesNotContain("\n", note.Title);
        string html = Ui.Markdown(note.Body).Value;
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<a ", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains(a.Toasts, t => t.Contains("conflict"));
    }

    [Fact(DisplayName = "What a sync writes into the board never names this computer's folders or user")]
    public async Task BoardWritesDoNotNameTheComputer()
    {
        Box a = NewBox("a");
        a.UserConnectsFolder(Storage);
        a.Attach();
        await a.Pass();
        a.Engine = _ => Task.FromResult(new SyncReport(SyncOutcome.Synced, null, [], [], [], [], [],
            [new SyncConflict("add-health-check/proposal.md", "description", "kept", "dropped text"), new SyncConflict("add-health-check/.openspec.yaml", "status", "In Review", "Blocked")], []));

        await a.Sync.SyncNow(a.Root);

        foreach (string file in AllFiles(a.Repo.PathOf("openspec/changes")))
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain(temp, text);
            Assert.DoesNotContain(a.Root, text);
            Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ═══ 4b. what the app renders from synced text ═══

    [Fact(DisplayName = "Raw HTML in a note, an event or a plan is rendered as text, never as markup")]
    public void RawHtmlIsNotRendered()
    {
        string html = Ui.Markdown("<script>alert(1)</script> <img src=x onerror=alert(1)> <iframe src=file:///C:/></iframe>").Value;

        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("<iframe", html);
    }

    [Fact(DisplayName = "A markdown link with a javascript: address is not rendered as a live link")]
    public void JavascriptLinksAreInert()
    {
        string html = Ui.Markdown("[click](javascript:alert(1)) and [data](data:text/html;base64,PHNjcmlwdD4=) and [vb](vbscript:x)").Value;

        Assert.DoesNotContain("href=\"javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"data:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"vbscript:", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "A note cannot make the app fetch an image from a remote address")]
    public void RemoteImagesAreNotFetched()
    {
        using TestRepo repo = new();
        string html = Ui.Markdown("![](https://attacker.example/pixel.png) ![](//attacker.example/p.png) ![](file://attacker/share/x.png)", repo.Root).Value;

        Assert.DoesNotContain("src=\"https://attacker.example", html);
        Assert.DoesNotContain("src=\"//attacker.example", html);
        Assert.DoesNotContain("src=\"file:", html);
    }

    [Fact(DisplayName = "An image link cannot read a file from outside the repository, by path or through a folder link")]
    public void ImageLinksStayInsideTheRepository()
    {
        using TestRepo repo = new();
        string outside = Path.Combine(temp, "outside-images");
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "private.png"), [1, 2, 3]);
        File.WriteAllBytes(repo.PathOf("inside.png"), [1, 2, 3]);
        string link = repo.PathOf("linked");
        Junction(link, outside);
        junctions.Add(link);

        Assert.NotNull(Ui.ImageSrc(repo.Root, "inside.png"));                                                   // the control
        foreach (string bad in new[] { "../outside-images/private.png", @"..\x.png", "C:/x.png", "/etc/x.png", @"\\attacker\share\x.png", "%2e%2e/x.png", "a/../../x.png" })
            Assert.Null(Ui.ImageSrc(repo.Root, bad));
        Assert.Null(Ui.ImageSrc(repo.Root, "linked/private.png"));                                              // inside by name, outside on disk
    }

    // ═══ 5. the files the sync trusts ═══

    [Fact(DisplayName = "Paths in server-writes.log outside the project's board are never counted as the app's edits")]
    public async Task ServerWritesAreConfinedToTheBoard()
    {
        Box a = NewBox("a");
        a.UserConnectsFolder(Storage);
        a.Attach();
        await a.Pass();
        using TestRepo other = new();
        string board = Path.Combine(a.Root, "openspec", "changes");
        byte[] log = Encoding.UTF8.GetBytes(string.Join("\n",
        [
            Path.Combine(board, "add-health-check", "proposal.md"),                     // the control: a path of the board
            @"C:\Windows\win.ini", @"..\..\x", Path.Combine(board, "..", "..", "outside.txt"), Path.Combine(board, "..", "config.yaml"),
            Path.Combine(other.Root, "openspec", "changes", "add-health-check", "proposal.md"),
            Path.Combine(a.Root, "openspec", "changes-evil", "x.md"),
            new string('a', 70_000),
        ]) + "\n");
        log = [.. log, 0xFF, 0xFE, 0xC0, 0xAF, (byte)'\n'];                              // and invalid UTF-8
        File.WriteAllBytes(Path.Combine(a.Data, ServerWrites.FileName), log);

        a.Do(() => a.Repo.Write(Item + "/proposal.md", "changed by git\n"));
        SyncReport? report = await a.Sync.SyncNow(a.Root);

        Assert.NotNull(report);
        IReadOnlySet<string> edited = a.SeenNow().Last().EditedInApp!;
        Assert.Contains("add-health-check/proposal.md", edited);
        Assert.All(edited, p => { Assert.False(Path.IsPathRooted(p)); Assert.False(p.StartsWith("..")); Assert.False(p.Split('/').Contains("..")); Assert.StartsWith("add-health-check/", p); });
    }

    [Fact(DisplayName = "One unusable line in server-writes.log does not stop the other lines from being taken")]
    public async Task OneBadLineDoesNotBlockTheServerLog()
    {
        Box a = NewBox("a");
        a.UserConnectsFolder(Storage);
        a.Attach();
        await a.Pass();
        string board = Path.Combine(a.Root, "openspec", "changes");
        string log = Path.Combine(a.Data, ServerWrites.FileName);
        File.WriteAllText(log, "bad \0 line\n" + Path.Combine(board, "add-health-check", "proposal.md") + "\n");   // any process of this user can append this

        SyncReport? first = await a.Sync.SyncNow(a.Root);
        SyncReport? second = await a.Sync.SyncNow(a.Root);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Multiple(
            () => Assert.Contains("add-health-check/proposal.md", a.SeenNow()[^2].EditedInApp!),        // the good line was read in the first of the two syncs
            () => Assert.Empty(Directory.EnumerateFiles(a.Data, ServerWrites.FileName + ".*.reading")));   // and the batch is not kept and re-read for ever
    }

    [Fact(DisplayName = "Rooted and parent-folder lines in edited-in-app.txt are not honoured")]
    public async Task EditedFileIsConfined()
    {
        Box a = NewBox("a");
        a.Settings.Connect(a.Root, "folder");
        a.Settings.SetValue(a.Root, "path", Storage);                                    // the project is connected, then the app is restarted over a planted file
        Directory.CreateDirectory(a.Settings.ProjectFolder(a.Root));
        File.WriteAllBytes(Path.Combine(a.Settings.ProjectFolder(a.Root), "edited-in-app.txt"),
            [.. Encoding.UTF8.GetBytes("../../evil\nC:\\evil\n/etc/passwd\n\\\\unc\\share\\x\nok/file.md\n"), 0xFF, 0xFE, (byte)'\n']);

        a.Attach();
        await a.Pass();

        IReadOnlySet<string> edited = a.SeenNow().First().EditedInApp!;
        Assert.Contains("ok/file.md", edited);
        Assert.All(edited, p => { Assert.False(Path.IsPathRooted(p), p); Assert.False(p.StartsWith(".."), p); });
    }

    // ═══ 6. .gitattributes ═══

    private Box GitBox(string name)
    {
        Box a = NewBox(name);
        Directory.CreateDirectory(a.Repo.PathOf(".git"));
        return a;
    }

    [Fact(DisplayName = "A .gitattributes that is a folder or read-only does not stop the project from opening and is left as it was")]
    public async Task GitAttributesThatCannotBeWrittenIsHarmless()
    {
        Box dir = GitBox("dir");
        Directory.CreateDirectory(dir.Repo.PathOf(".gitattributes"));
        dir.UserConnectsFolder(Path.Combine(temp, "s1"));
        dir.Attach();
        await dir.Pass();
        Assert.True(Directory.Exists(dir.Repo.PathOf(".gitattributes")));
        Assert.Contains(dir.Toasts, t => t.Contains(".gitattributes"));

        Box ro = GitBox("ro");
        ro.Repo.Write(".gitattributes", "*.png binary\n");
        File.SetAttributes(ro.Repo.PathOf(".gitattributes"), FileAttributes.ReadOnly);
        ro.UserConnectsFolder(Path.Combine(temp, "s2"));
        ro.Attach();
        await ro.Pass();
        Assert.Equal("*.png binary\n", File.ReadAllText(ro.Repo.PathOf(".gitattributes")));
        File.SetAttributes(ro.Repo.PathOf(".gitattributes"), FileAttributes.Normal);
        Assert.True(ro.Sync.Runs(ro.Root) > 0);                           // the sync itself went on
    }

    [Fact(DisplayName = "A .gitattributes that is a symbolic link is not written through to the file it points at")]
    public async Task GitAttributesSymlinkIsNotFollowed()
    {
        // Needs the privilege to create a symbolic link (Developer Mode or an administrator). Without it the test cannot run and returns early: UNPROVEN on such a machine.
        Box a = GitBox("a");
        string target = Path.Combine(temp, "victim.txt");
        File.WriteAllText(target, "precious\n");
        try { File.CreateSymbolicLink(a.Repo.PathOf(".gitattributes"), target); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return; }
        a.UserConnectsFolder(Path.Combine(temp, "s3"));

        a.Attach();
        await a.Pass();

        Assert.Equal("precious\n", File.ReadAllText(target));
    }

    // ═══ 7. the shell ═══

    [Fact(DisplayName = "The trash folder the settings page opens is always a folder inside this project's sync folder")]
    public async Task TrashFolderIsOurOwnFolder()
    {
        Box a = NewBox("a");
        a.UserConnectsFolder(Storage);
        a.Attach();
        await a.Pass();
        Assert.Null(a.Sync.Status(a.Root).TrashFolder);                                   // nothing is offered until the folder exists
        Directory.CreateDirectory(Path.Combine(a.Settings.StateFolder(a.Root), "trash"));

        string? trash = a.Sync.Status(a.Root).TrashFolder;

        Assert.NotNull(trash);
        Assert.True(Directory.Exists(trash));
        Assert.StartsWith(a.Settings.ProjectFolder(a.Root) + Path.DirectorySeparatorChar, trash);
    }

    // ═══ the engine and a hostile storage ═══

    [Fact(DisplayName = "A folder link inside the board is not followed out of the board and its files are not uploaded")]
    public async Task JunctionInTheBoardIsNotFollowed()
    {
        string board = Path.Combine(temp, "board"), outside = Path.Combine(temp, "outside"), state = Path.Combine(temp, "state");
        Directory.CreateDirectory(board);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(board, "own.md"), "inside the board\n");
        File.WriteAllText(Path.Combine(outside, "private.txt"), "OUTSIDE-THE-BOARD-PRIVATE\n");
        string link = Path.Combine(board, "linked");
        Junction(link, outside);
        junctions.Add(link);
        IBoardStorage storage = BoardSyncApi.FolderStorage(Storage);

        SyncReport report = await BoardSyncApi.Sync(new SyncOptions(board, state, storage, "dev-a"));

        Assert.Equal(SyncOutcome.Synced, report.Outcome);
        Assert.DoesNotContain(AllFiles(Storage), f => Encoding.UTF8.GetString(File.ReadAllBytes(f)).Contains("OUTSIDE-THE-BOARD-PRIVATE"));
        Assert.DoesNotContain(report.Uploaded, p => p.StartsWith("linked/"));
    }

    [Fact(DisplayName = "A bucket listing that carries a DTD with nested entities is refused, not expanded")]
    public async Task ListingWithEntityBombIsRefused()
    {
        StringBuilder dtd = new("<!ENTITY a \"xxxxxxxxxx\">");
        for (char c = 'b'; c <= 'g'; c++) dtd.Append($"<!ENTITY {c} \"{string.Concat(Enumerable.Repeat($"&{(char)(c - 1)};", 10))}\">");   // 10 characters grow to 10 million
        string xml = $"<?xml version=\"1.0\"?><!DOCTYPE r [{dtd}]><ListBucketResult><IsTruncated>false</IsTruncated><Contents><Key>&g;</Key><ETag>e</ETag></Contents></ListBucketResult>";
        IBoardStorage storage = BoardSyncApi.S3Storage("https://storage.attacker.example", "b", null, "k", "s", "auto",
            new Canned(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "application/xml") }));

        IReadOnlyList<StoredEntry> entries;
        try { entries = await storage.List(""); }
        catch (BoardStorageException) { return; }                                          // refused: safe

        Assert.True(entries.All(e => e.Path.Length < 1000), $"the listing was expanded to a key of {entries.Max(e => e.Path.Length):N0} characters");
    }

    // ═══ the fixes: approval, bound keys, address rules, safe links, back-off ═══

    private static AngleSharp.Dom.IElement StorageSection(IRenderedComponent<SettingsPage> page) => page.Find("section[data-storage]");

    [Fact(DisplayName = "A storage that only the repository names is shown with its full address and an Allow button, and nothing syncs")]
    public async Task ProposalIsShownAndNothingSyncs()
    {
        Box a = NewBox("a");
        string path = Path.Combine(temp, "proposed-share");
        a.RepoSays("provider: folder", "path: " + Q(path));
        await using BunitContext ctx = PageContext(a);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        Assert.Equal($"This project asks to keep its board in step through: Folder  {path}. Allow it on this computer?", StorageSection(page).QuerySelector(".bs-state")!.TextContent.Trim());
        Assert.NotNull(StorageSection(page).QuerySelector("button.bs-approve"));
        Assert.NotNull(StorageSection(page).QuerySelector("button.bs-later"));
        Assert.Empty(StorageSection(page).QuerySelectorAll("[data-field]"));       // nothing can be edited until it is allowed or dropped
        await a.Pass();
        Assert.False(Directory.Exists(path));
        Assert.Equal(0, a.Sync.Runs(a.Root));
        Assert.Contains(a.Toasts, t => t.Contains("Settings") && t.Contains("Board storage"));    // one toast on open points to the settings
    }

    [Fact(DisplayName = "Allowing the proposed storage on this computer starts the sync, and Not now keeps it inert")]
    public async Task AllowStartsTheSyncNotNowDoesNot()
    {
        Box a = NewBox("a");
        string path = Path.Combine(temp, "proposed-share");
        a.RepoSays("provider: folder", "path: " + Q(path));
        await using BunitContext ctx = PageContext(a);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        await page.InvokeAsync(() => StorageSection(page).QuerySelector("button.bs-later")!.Click());
        page.WaitForAssertion(() => Assert.Null(StorageSection(page).QuerySelector("button.bs-later")));
        Assert.NotNull(StorageSection(page).QuerySelector("button.bs-approve"));
        await a.Pass();
        Assert.False(File.Exists(Path.Combine(path, "manifest.json")));

        await page.InvokeAsync(() => StorageSection(page).QuerySelector("button.bs-approve")!.Click());
        page.WaitForAssertion(() => Assert.Null(StorageSection(page).QuerySelector("button.bs-approve")));   // the page redrew: the approval was taken and its sync was started
        await a.Pass();

        Assert.True(File.Exists(Path.Combine(path, "manifest.json")));
        Assert.Contains("data-field", page.Markup);
    }

    [Fact(DisplayName = "A UNC path or an S3 address is described plainly, with its host, bucket and prefix")]
    public void ProposalNamesHostBucketAndPrefix()
    {
        Box a = NewBox("a");
        a.RepoSays("provider: folder", @"path: '\\fileserver.example\share\board'");
        StorageProposal unc = a.Settings.Proposal(a.Root)!;
        Assert.Contains(@"\\fileserver.example\share\board", unc.Line);
        Assert.Contains("network share on fileserver.example", unc.Detail);

        a.RepoSays("provider: s3", "endpoint: https://acct.r2.cloudflarestorage.com", "bucket: team-board", "prefix: boards/main");
        StorageProposal s3 = a.Settings.Proposal(a.Root)!;
        Assert.Contains("https://acct.r2.cloudflarestorage.com", s3.Line);
        Assert.Contains("acct.r2.cloudflarestorage.com", s3.Detail);
        Assert.Contains("team-board", s3.Detail);
        Assert.Contains("boards/main", s3.Detail);
    }

    [Fact(DisplayName = "Going back to this folder only forgets the approval, so the same storage is asked about again")]
    public void DisconnectRemovesTheApproval()
    {
        Box a = NewBox("a");
        a.UserConnectsFolder(Storage);
        Assert.Null(a.Settings.Proposal(a.Root));                                      // chosen here: approved here
        Assert.True(a.Settings.Disconnect(a.Root).Success);

        a.RepoSays("provider: folder", "path: " + Q(Storage));                          // the repository names it again

        Assert.NotNull(a.Settings.Proposal(a.Root));
        Assert.False(File.Exists(Path.Combine(a.Settings.ProjectFolder(a.Root), "approval.json")));
    }

    [Fact(DisplayName = "A storage chosen in this app's settings is approved on this computer, and changing the address there keeps it approved")]
    public void ChoosingHereApproves()
    {
        Box a = NewBox("a");
        a.UserConnectsFolder(Storage);
        Assert.Null(a.Settings.Proposal(a.Root));
        Assert.True(a.Settings.SetValue(a.Root, "path", Path.Combine(temp, "elsewhere")).Success);
        Assert.Null(a.Settings.Proposal(a.Root));
        Assert.Null(a.Settings.Problem(a.Root));
    }

    [Fact(DisplayName = "Editing the proposed storage's values is refused until it is allowed, so a proposal cannot be approved by editing one field")]
    public void ProposalCannotBeApprovedByEditing()
    {
        Box a = NewBox("a");
        a.RepoSays("provider: s3", "endpoint: https://attacker.example", "bucket: b");

        Assert.False(a.Settings.SetValue(a.Root, "bucket", "other").Success);
        Assert.NotNull(a.Settings.Proposal(a.Root));
        Assert.True(a.Settings.Connect(a.Root, "s3").Success);                          // choosing the provider afresh starts from nothing
        Assert.DoesNotContain("attacker", File.ReadAllText(a.ConfigFile));
    }

    [Fact(DisplayName = "An old credentials file with a plain access key id is rewritten protected and its keys keep working")]
    public void PlainAccessKeyIdIsMigrated()
    {
        Box a = NewBox("a");
        Assert.True(a.Settings.Connect(a.Root, "s3").Success);
        Assert.True(a.Settings.SetValue(a.Root, "endpoint", "https://storage.legit.example").Success);
        Assert.True(a.Settings.SetValue(a.Root, "bucket", "board").Success);
        string file = Path.Combine(a.Settings.ProjectFolder(a.Root), "credentials.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(new { Plain = new Dictionary<string, string> { ["accessKey"] = KeyId }, Protected = new Dictionary<string, string>() }));

        BoardStorageSettings fresh = new(a.Data, StorageProviders.Builtin);

        Assert.Equal(KeyId, fresh.ComputerValue(a.Root, "accessKey"));
        Assert.DoesNotContain(KeyId, File.ReadAllText(file));
        Assert.Null(fresh.Proposal(a.Root));                                                // it was set up in the app before: no new question
    }

    [Fact(DisplayName = "Keys follow an address the user edits in this app, but not one the repository changes")]
    public void KeysAreBoundToTheirAddress()
    {
        Box a = NewBox("a");
        a.UserConnectsS3("https://storage.legit.example");
        Assert.True(a.Settings.SetValue(a.Root, "prefix", "boards/main").Success);          // edited here: the keys come along
        Assert.Null(a.Settings.Problem(a.Root));

        File.WriteAllText(a.ConfigFile, File.ReadAllText(a.ConfigFile).Replace("storage.legit.example", "storage.other.example"));
        Assert.NotNull(a.Settings.Proposal(a.Root));
        Assert.True(a.Settings.Approve(a.Root).Success);                                    // even when it is allowed ...

        Assert.Equal("Waiting for Access key ID, Secret access key for the new address on this computer", a.Settings.Problem(a.Root));
        Assert.False(a.Settings.IsSet(a.Root, "secretKey"));                                // ... the old keys are not used for it
        Assert.Null(a.Settings.Open(a.Root).Storage);
        Assert.True(a.Settings.SetValue(a.Root, "accessKey", KeyId).Success);
        Assert.True(a.Settings.SetValue(a.Root, "secretKey", Secret).Success);
        Assert.Null(a.Settings.Problem(a.Root));
    }

    [Theory(DisplayName = "An S3 address that is unsafe or ambiguous is refused with a message that says what to change")]
    [InlineData("http://storage.attacker.example", "board", "", "https")]
    [InlineData("https://key:secret@storage.example", "board", "", "user name")]
    [InlineData("https://storage.example/?x=1", "board", "", "query")]
    [InlineData("https://storage.example/#frag", "board", "", "query")]
    [InlineData("https://storage.example", "..", "", "\"..\"")]
    [InlineData("https://storage.example", "board", "a/../b", "\"..\"")]
    [InlineData("https://storage.example", "board", "a/./b", "\".\"")]
    [InlineData("ftp://storage.example", "board", "", "web address")]
    public void UnsafeS3AddressIsRefused(string endpoint, string bucket, string prefix, string mentions)
    {
        Box a = NewBox("a");
        Assert.True(a.Settings.Connect(a.Root, "s3").Success);
        a.Settings.SetValue(a.Root, "endpoint", endpoint);
        a.Settings.SetValue(a.Root, "bucket", bucket);
        a.Settings.SetValue(a.Root, "prefix", prefix);
        a.Settings.SetValue(a.Root, "accessKey", KeyId);
        a.Settings.SetValue(a.Root, "secretKey", Secret);

        (BoardSync.PublicDTOs.IBoardStorage? storage, string? problem) = a.Settings.Open(a.Root);
        (storage as IDisposable)?.Dispose();

        Assert.Null(storage);
        Assert.Contains(mentions, problem, StringComparison.OrdinalIgnoreCase);
    }

    [Theory(DisplayName = "http is accepted for this computer's own address only")]
    [InlineData("http://localhost:9000")]
    [InlineData("http://127.0.0.1:9000")]
    [InlineData("http://[::1]:9000")]
    public void LoopbackHttpIsAccepted(string endpoint)
    {
        Box a = NewBox("a");
        a.UserConnectsS3(endpoint);

        (BoardSync.PublicDTOs.IBoardStorage? storage, string? problem) = a.Settings.Open(a.Root);
        (storage as IDisposable)?.Dispose();

        Assert.Null(problem);
        Assert.NotNull(storage);
    }

    [Fact(DisplayName = "A storage folder that is relative, inside the board, around the board or inside .git is refused")]
    public void UnsafeFoldersAreRefused()
    {
        Box a = NewBox("a");
        string board = a.Repo.PathOf("openspec/changes");
        foreach ((string path, string mentions) in new[]
        {
            (@"relative\folder", "absolute"), (@"..\x", "absolute"), (@"C:x", "absolute"), (@"\x", "absolute"),
            (board, "is inside this project's board"), (Path.Combine(board, "sub"), "is inside this project's board"), (a.Repo.Root, "contains this project's board"), (a.Repo.PathOf("openspec"), "contains this project's board"),
            (Path.Combine(a.Repo.Root, ".git"), ".git"), (Path.Combine(a.Repo.Root, ".git", "objects"), ".git"),
        })
        {
            a.UserConnectsFolder(path);
            (BoardSync.PublicDTOs.IBoardStorage? storage, string? problem) = a.Settings.Open(a.Root);
            (storage as IDisposable)?.Dispose();
            Assert.True(storage is null, path);
            Assert.True(problem is not null && problem.Contains(mentions, StringComparison.OrdinalIgnoreCase), $"{path}: {problem}");
        }
        a.UserConnectsFolder(Path.Combine(temp, "fine"));
        Assert.Null(a.Settings.Problem(a.Root));
    }

    [Fact(DisplayName = "A config.yaml whose board-storage section cannot be read says so on the settings page and the project opens")]
    public async Task UnreadableSectionIsShown()
    {
        Box a = NewBox("a");
        a.RepoSays("provider:");
        await using BunitContext ctx = PageContext(a);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        Assert.Contains("could not be read", StorageSection(page).QuerySelector(".bs-state")!.TextContent);
        Assert.Contains("config.yaml", StorageSection(page).QuerySelector(".bs-state")!.TextContent);
        Assert.False(a.Sync.IsActive(a.Root));
    }

    [Fact(DisplayName = "Only the last four characters of an access key id appear in what the app shows")]
    public async Task KeyIdIsShownAsItsLastFour()
    {
        (_, List<string> messages) = await FailingStorage();

        Assert.Contains(messages, m => m.Contains(KeyId[^4..]));
        Assert.DoesNotContain(messages, m => m.Contains(KeyId[..^4]));
    }

    [Fact(DisplayName = "Links to the board's own items, notes, documents and files stay live, and a repository image is still shown")]
    public void InternalLinksAndRepositoryImagesStayLive()
    {
        using TestRepo repo = new();
        repo.Write("docs/diagram.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"4\" height=\"4\"/>");
        string html = Ui.Markdown("[item](item/123) [note](note/4) [doc](docs/guide.md) [anchor](#top) [web](https://example.com/x) [mail](mailto:a@b.example)\n\n![d](docs/diagram.svg)\n\nwww.example.com", repo.Root).Value;

        foreach (string href in new[] { "item/123", "note/4", "docs/guide.md", "#top", "https://example.com/x", "mailto:a@b.example", "http://www.example.com" })
            Assert.Contains($"href=\"{href}\"", html);
        Assert.Contains("<img src=\"data:image/svg+xml;base64,", html);
    }

    [Fact(DisplayName = "An image that is not a file of the repository shows as a link with its text, or as plain text, never as an image")]
    public void OtherImagesBecomeLinksOrText()
    {
        using TestRepo repo = new();
        string html = Ui.Markdown("![remote](https://example.com/p.png) ![gone](docs/missing.png) ![datauri](data:image/png;base64,AAAA) ![local](file:///C:/x.png)", repo.Root).Value;
        string plain = Ui.Markdown("![remote](https://example.com/p.png)").Value;

        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("<img", plain);
        Assert.Contains("<a href=\"https://example.com/p.png\">remote</a>", html);
        Assert.Contains("<a href=\"docs/missing.png\">gone</a>", html);
        Assert.DoesNotContain("href=\"data:", html);
        Assert.DoesNotContain("href=\"file:", html);
        Assert.Contains("datauri", html);
        Assert.Contains("local", html);
    }

    [Fact(DisplayName = "An image file over the size cap is not inlined")]
    public void BigImagesAreNotInlined()
    {
        using TestRepo repo = new();
        File.WriteAllBytes(repo.PathOf("small.png"), new byte[1000]);
        File.WriteAllBytes(repo.PathOf("big.png"), new byte[Ui.MaxImageBytes + 1]);

        Assert.NotNull(Ui.ImageSrc(repo.Root, "small.png"));
        Assert.Null(Ui.ImageSrc(repo.Root, "big.png"));
    }

    [Fact(DisplayName = "A bad line or a huge server-writes.log never blocks the sync, and a huge log is replaced")]
    public async Task HugeServerLogIsDropped()
    {
        Box a = NewBox("a");
        a.UserConnectsFolder(Storage);
        a.Attach();
        await a.Pass();
        string log = Path.Combine(a.Data, ServerWrites.FileName);
        using (FileStream fs = File.Create(log))
        {
            byte[] line = Encoding.UTF8.GetBytes(new string('x', 1000) + "\n");
            for (int i = 0; i < 17_500; i++) fs.Write(line);                              // more than 16 MB
        }

        SyncReport? report = await a.Sync.SyncNow(a.Root);

        Assert.NotNull(report);
        Assert.False(File.Exists(log));                                                       // unreadable beyond the cap: replaced, not read
        Assert.Empty(Directory.EnumerateFiles(a.Data, ServerWrites.FileName + ".*.reading"));
    }
}
