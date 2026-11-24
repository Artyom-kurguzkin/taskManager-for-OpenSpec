using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Radzen;
using TaskManager.AppState;
using TaskManager.Components.Pages;
using TaskManager.Helpers;

namespace TaskManager.Tests;

/// <summary>
/// The app keeps itself up to date: on start it asks GitHub for the latest release, Settings shows the version and what the check found, and an app that runs from a release folder
/// downloads the newer release, offers a restart and swaps its files after it exits. The network is a fake handler, the install and update folders are temp folders, and the helper that swaps
/// the files runs against a dummy process: nothing here reaches GitHub or touches a real exe.
/// </summary>
public sealed class UpdatingTheAppTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "rtm-update-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly TestRepo repo = new();
    private string Install => Path.Combine(root, "app");
    private string ExePath => Path.Combine(Install, "TaskManager.exe");
    private string Updates => Path.Combine(root, "updates");

    public UpdatingTheAppTests() => Directory.CreateDirectory(Install);

    public void Dispose()
    {
        repo.Dispose();
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    // ── the fake network ──

    private sealed class FakeGitHub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            lock (Requests) Requests.Add(request);
            return answer(request, cancel);
        }
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string ReleaseJson(string tag, byte[]? zip = null, string assetName = "RepoTaskManager-win-x64.zip", string? digest = null) =>
        $$"""{"tag_name":"{{tag}}","assets":[{"name":"{{assetName}}","browser_download_url":"https://downloads.invalid/{{tag}}/{{assetName}}","digest":{{(zip is null && digest is null ? "null" : "\"" + (digest ?? "sha256:" + Convert.ToHexStringLower(SHA256.HashData(zip!))) + "\"")}}}]}""";

    /// <summary>A GitHub that answers the release query with <paramref name="release"/> and the asset download with <paramref name="zip"/>.</summary>
    private static FakeGitHub GitHub(string release, byte[]? zip = null) => new((request, _) =>
        Task.FromResult(request.RequestUri!.Host == "api.github.com" ? Json(release) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip ?? []) }));

    private static byte[] Zip(params (string Name, string Text)[] files)
    {
        using MemoryStream memory = new();
        using (ZipArchive zip = new(memory, ZipArchiveMode.Create, leaveOpen: true))
            foreach ((string name, string text) in files)
                using (StreamWriter w = new(zip.CreateEntry(name).Open())) w.Write(text);
        return memory.ToArray();
    }

    private static byte[] Release(string exeText = "new exe") => Zip(("TaskManager.exe", exeText), ("wwwroot/index.html", "new page"));

    // ── the app under test ──

    private sealed class Launches { public List<UpdateApplication> Applied { get; } = []; public int Restarts; }

    private AppUpdates NewUpdates(FakeGitHub github, string? version = "0.1.5", bool debug = false, string? exe = null, UiService? ui = null, Launches? launches = null, TimeSpan? timeout = null)
    {
        launches ??= new();
        AppUpdates updates = new(new HttpClient(github), ui ?? new UiService { ToastLifetime = Timeout.InfiniteTimeSpan },
            new UpdateSetup(version, exe ?? ExePath, debug, Updates, new Uri("https://api.github.com/repos/Artyom-kurguzkin/taskManager-for-OpenSpec/releases/latest"), timeout ?? TimeSpan.FromSeconds(10)),
            apply => { launches.Applied.Add(apply); return true; });
        updates.RestartRequested += () => launches.Restarts++;
        return updates;
    }

    private static async Task<DialogRequest> DialogAsync(UiService ui)
    {
        TaskCompletionSource<DialogRequest> got = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void On() { if (ui.Dialog is { } d) got.TrySetResult(d); }
        ui.Changed += On;
        On();
        try { return await got.Task.WaitAsync(WaitThen.Bound); }
        finally { ui.Changed -= On; }
    }

    private ILoggerFactory StartLog()
    {
        ILoggerFactory factory = AppLog.Create(Path.Combine(root, "logs"), console: false, LogLevel.Information, new FakeTimeProvider(new DateTimeOffset(2026, 11, 24, 9, 8, 7, TimeSpan.Zero)), pid: 5, appFolder: Path.Combine(root, "logs"));
        AppLog.Use(factory);
        return factory;
    }

    // ── what the check finds ──

    [Fact(DisplayName = "A newer release on main is reported as available")]
    public async Task ANewerReleaseIsAvailable()
    {
        FakeGitHub github = GitHub(ReleaseJson("v0.1.9", Release()));
        AppUpdates updates = NewUpdates(github, version: "0.1.5+3f2a1b9c", debug: true);   // a development build asks and reports, it never installs

        await updates.CheckAsync();

        Assert.Equal("0.1.5", updates.Current);   // the +commit part is not part of the version
        Assert.Equal(UpdateState.Available, updates.State);
        Assert.Equal("0.1.9", updates.Latest);
        HttpRequestMessage ask = Assert.Single(github.Requests);   // asked once, nothing downloaded
        Assert.Equal("https://api.github.com/repos/Artyom-kurguzkin/taskManager-for-OpenSpec/releases/latest", ask.RequestUri!.ToString());
        Assert.Equal("RepoTaskManager", ask.Headers.UserAgent.ToString());

        // a build made between releases (a prerelease of 0.1.9) is older than the release 0.1.9
        AppUpdates between = NewUpdates(GitHub(ReleaseJson("v0.1.9")), version: "0.1.9-g3f2a1b9", debug: true);
        await between.CheckAsync();
        Assert.Equal(UpdateState.Available, between.State);
    }

    [Fact(DisplayName = "The same or an older release reports the app as up to date")]
    public async Task TheSameOrOlderIsUpToDate()
    {
        foreach (string tag in new[] { "v0.1.5", "v0.1.2", "v0.1" })
        {
            AppUpdates updates = NewUpdates(GitHub(ReleaseJson(tag)));
            await updates.CheckAsync();
            Assert.Equal(UpdateState.UpToDate, updates.State);
            Assert.Equal("Up to date", updates.Summary);
        }
    }

    [Fact(DisplayName = "No network or a bad answer says the check could not be made and does not stop the app")]
    public async Task ACheckThatFailsIsNamedNotThrown()
    {
        StartLog();
        FakeGitHub offline = new((_, _) => throw new HttpRequestException("No such host is known."));
        FakeGitHub busy = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));
        FakeGitHub garbage = new((_, _) => Task.FromResult(Json("<html>not json</html>")));
        FakeGitHub noAsset = GitHub(ReleaseJson("v0.1.9", assetName: "something-else.zip", digest: "sha256:00"));
        FakeGitHub hangs = new(async (_, cancel) => { await Task.Delay(Timeout.InfiniteTimeSpan, cancel); return new HttpResponseMessage(); });   // never answers: the timeout ends it

        foreach (FakeGitHub github in new[] { offline, busy, garbage, noAsset, hangs })
        {
            AppUpdates updates = NewUpdates(github, timeout: TimeSpan.FromMilliseconds(100));
            await updates.CheckAsync();   // never throws
            Assert.Equal(UpdateState.Failed, updates.State);
            Assert.StartsWith("Couldn't check", updates.Summary);
            Assert.False(Directory.Exists(Updates));
        }
        AppUpdates named = NewUpdates(offline);
        await named.CheckAsync();
        Assert.Contains("No such host", named.Summary);   // the reason is shown
        Assert.Contains(ReadLogs(), l => l.Contains("Could not check for an update") && l.Contains("No such host"));
    }

    private string[] ReadLogs() => Directory.GetFiles(Path.Combine(root, "logs"), "*.log").SelectMany(f => ReadLog(f).Split('\n')).ToArray();

    [Fact(DisplayName = "A development build shows its version and never updates itself")]
    public async Task ADevelopmentBuildNeverUpdates()
    {
        string underBin = Path.Combine(root, "repo", "TaskManager", "bin", "Release", "net10.0", "TaskManager.exe");
        (string? Version, bool Debug, string Exe, string Label)[] builds =
        [
            ("0.1.5", true, ExePath, "Version 0.1.5 (development build)"),
            (null, false, ExePath, "Version unknown (development build)"),
            ("0.1.5-g3f2a1b9", false, ExePath, "Version 0.1.5-g3f2a1b9 (development build)"),
            ("0.1.5", false, underBin, "Version 0.1.5 (development build)"),
            ("0.1.5", false, Path.Combine(Install, "testhost.exe"), "Version 0.1.5 (development build)"),
        ];
        foreach ((string? version, bool debug, string exe, string label) in builds)
        {
            FakeGitHub github = GitHub(ReleaseJson("v9.0.0", Release()));
            Launches launches = new();
            AppUpdates updates = NewUpdates(github, version, debug, exe, launches: launches);

            await updates.CheckAsync();

            Assert.Equal(label, updates.VersionLabel);
            Assert.False(updates.CanUpdate);
            Assert.NotNull(updates.WhyNot);
            Assert.Single(github.Requests);   // the release was asked about, never downloaded
            Assert.Empty(launches.Applied);
            Assert.False(Directory.Exists(Updates));
            if (version is not null) Assert.Equal(UpdateState.Available, updates.State);
        }
    }

    [Fact(DisplayName = "The Settings page shows the current version and the update state")]
    public async Task SettingsShowsTheVersion()
    {
        FakeGitHub github = GitHub(ReleaseJson("v0.1.9", Release()));
        AppUpdates updates = NewUpdates(github, version: "0.1.5", debug: true);
        await using BunitContext ctx = new();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(Path.Combine(root, "settings.json")));
        ctx.Services.AddSingleton<Workspace>();
        ctx.Services.AddSingleton(updates);
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        IRenderedComponent<SettingsPage> page = ctx.Render<SettingsPage>();

        AngleSharp.Dom.IElement row = page.Find("[data-setting='version']");
        Assert.Equal("Version", row.QuerySelector("th")!.TextContent.Trim());
        Assert.Contains("Version 0.1.5 (development build)", row.QuerySelector(".version-current")!.TextContent);
        Assert.Equal("Not checked yet", row.QuerySelector(".version-state")!.TextContent.Trim());

        page.ClickWhenReady("[data-setting='version'] button.check-now");

        page.Eventually(() => Assert.Contains("0.1.9 is available", page.Find("[data-setting='version'] .version-state").TextContent));
        Assert.Equal("Check now", page.Find("[data-setting='version'] button.check-now").TextContent.Trim());
    }

    // ── installing ──

    [Fact(DisplayName = "Out of date, the app downloads the release, checks it holds TaskManager.exe and offers to restart")]
    public async Task DownloadsAndOffersARestart()
    {
        byte[] zip = Release("brand new exe");
        FakeGitHub github = GitHub(ReleaseJson("v0.1.9", zip), zip);
        UiService ui = new() { ToastLifetime = Timeout.InfiniteTimeSpan };
        Launches launches = new();
        AppUpdates updates = NewUpdates(github, ui: ui, launches: launches);

        Task check = updates.CheckAsync();
        DialogRequest offer = await DialogAsync(ui);

        Assert.Contains(ui.Toasts, t => t.Text == "Updating to v0.1.9…");
        Assert.Equal("Restart now", offer.Yes);
        Assert.Equal("Later", offer.No);
        Assert.Contains("0.1.9", offer.Title + offer.Text);
        string staged = Path.Combine(Updates, "v0.1.9", "files");
        Assert.Equal("brand new exe", File.ReadAllText(Path.Combine(staged, "TaskManager.exe")));
        Assert.True(File.Exists(Path.Combine(staged, "wwwroot", "index.html")));
        Assert.Equal(UpdateState.Ready, updates.State);
        Assert.Empty(launches.Applied);   // nothing is swapped before the user answers

        ui.Close("yes");
        await check;

        UpdateApplication apply = Assert.Single(launches.Applied);
        Assert.Equal(staged, apply.Staged);
        Assert.Equal(Install, apply.Install);
        Assert.Equal(Path.Combine(Updates, "previous"), apply.Backup);
        Assert.Equal(Environment.ProcessId, apply.ProcessId);
        Assert.Equal(ExePath, apply.RestartExe);
        Assert.Equal(1, launches.Restarts);
        Assert.False(File.Exists(ExePath));   // the app's own files are the helper's to swap, after it exits
    }

    [Fact(DisplayName = "Choosing Later installs the downloaded release when the app exits, without starting it again")]
    public async Task LaterInstallsOnExit()
    {
        byte[] zip = Release();
        UiService ui = new() { ToastLifetime = Timeout.InfiniteTimeSpan };
        Launches launches = new();
        AppUpdates updates = NewUpdates(GitHub(ReleaseJson("v0.1.9", zip), zip), ui: ui, launches: launches);

        Task check = updates.CheckAsync();
        await DialogAsync(ui);
        ui.Close(null);
        await check;

        Assert.Empty(launches.Applied);
        Assert.Equal(0, launches.Restarts);
        Assert.Contains("when you close the app", updates.Summary);

        updates.ApplyOnExit();
        updates.ApplyOnExit();   // once

        UpdateApplication apply = Assert.Single(launches.Applied);
        Assert.Null(apply.RestartExe);
    }

    [Fact(DisplayName = "A download that does not match its published checksum or lacks TaskManager.exe is refused")]
    public async Task ABadDownloadIsRefused()
    {
        byte[] good = Release();
        byte[] noExe = Zip(("wwwroot/index.html", "page"));
        foreach ((string release, byte[] bytes, string reason) in new (string, byte[], string)[]
        {
            (ReleaseJson("v0.1.9", good, digest: "sha256:" + new string('0', 64)), good, "checksum"),
            (ReleaseJson("v0.1.9", noExe), noExe, "TaskManager.exe"),
            (ReleaseJson("v0.1.9", [1, 2, 3]), [1, 2, 3], "zip"),
        })
        {
            UiService ui = new() { ToastLifetime = Timeout.InfiniteTimeSpan };
            Launches launches = new();
            AppUpdates updates = NewUpdates(GitHub(release, bytes), ui: ui, launches: launches);

            await updates.CheckAsync();

            Assert.Equal(UpdateState.Failed, updates.State);
            Assert.Contains(reason, updates.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Null(ui.Dialog);
            Assert.Empty(launches.Applied);
            Assert.False(Directory.Exists(Path.Combine(Updates, "v0.1.9")));   // a refused download leaves nothing behind
        }
    }

    // ── the helper that swaps the files ──

    private static string ReadLog(string file) { using FileStream s = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using StreamReader r = new(s); return r.ReadToEnd(); }

    private (string Staged, string Backup, string Log) Prepare()
    {
        string staged = Path.Combine(root, "staged");
        Directory.CreateDirectory(Path.Combine(staged, "wwwroot"));
        File.WriteAllText(Path.Combine(staged, "TaskManager.exe"), "new");
        File.WriteAllText(Path.Combine(staged, "wwwroot", "index.html"), "new page");
        File.WriteAllText(ExePath, "old");
        File.WriteAllText(Path.Combine(Install, "keep.txt"), "mine");
        return (staged, Path.Combine(root, "backup"), Path.Combine(root, "apply.log"));
    }

    /// <summary>A stand-in for the running app: a process that ends when the gate file appears.</summary>
    private static Process Dummy(string gate) => Process.Start(new ProcessStartInfo("powershell.exe", ["-NoProfile", "-Command", $"while (-not (Test-Path -LiteralPath '{gate}')) {{ Start-Sleep -Milliseconds 50 }}"]) { CreateNoWindow = true, UseShellExecute = false })!;

    [Fact(DisplayName = "The update helper waits for the app to exit, swaps the files and keeps a backup")]
    public async Task TheHelperSwapsAfterTheAppExits()
    {
        (string staged, string backup, string log) = Prepare();
        string gate = Path.Combine(root, "gate");
        using Process app = Dummy(gate);
        using Process helper = UpdateHelper.Launch(new UpdateApplication(staged, Install, backup, app.Id, RestartExe: null), log, retries: 2)!;

        while (!(File.Exists(log) && ReadLog(log).Contains($"Waiting for process {app.Id}"))) await Task.Delay(20);   // the helper is up and waiting
        Assert.Equal("old", File.ReadAllText(ExePath));   // the app still runs: nothing is swapped

        File.WriteAllText(gate, "");   // the app exits
        await helper.WaitForExitAsync().WaitAsync(WaitThen.Bound);

        Assert.True(helper.ExitCode == 0, ReadLog(log));
        Assert.Equal("new", File.ReadAllText(ExePath));
        Assert.Equal("new page", File.ReadAllText(Path.Combine(Install, "wwwroot", "index.html")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(Install, "keep.txt")));   // what the release does not hold stays
        Assert.Equal("old", File.ReadAllText(Path.Combine(backup, "TaskManager.exe")));   // one rollback
        Assert.Contains("Updated", ReadLog(log));
    }

    [Fact(DisplayName = "A swap that cannot finish puts the old files back")]
    public async Task AFailedSwapRestoresTheBackup()
    {
        (string staged, string backup, string log) = Prepare();
        string gate = Path.Combine(root, "gate");
        using Process app = Dummy(gate);
        using FileStream lockedExe = new(ExePath, FileMode.Open, FileAccess.Read, FileShare.Read);   // something still holds the exe: it cannot be replaced
        File.WriteAllText(Path.Combine(staged, "wwwroot", "index.html"), "half way");
        Directory.CreateDirectory(Path.Combine(Install, "wwwroot"));
        File.WriteAllText(Path.Combine(Install, "wwwroot", "index.html"), "old page");
        File.WriteAllText(gate, "");
        using Process helper = UpdateHelper.Launch(new UpdateApplication(staged, Install, backup, app.Id, RestartExe: null), log, retries: 1)!;

        await helper.WaitForExitAsync().WaitAsync(WaitThen.Bound);

        Assert.NotEqual(0, helper.ExitCode);
        Assert.Equal("old page", File.ReadAllText(Path.Combine(Install, "wwwroot", "index.html")));   // put back as it was
        Assert.Contains("restored", ReadLog(log), StringComparison.OrdinalIgnoreCase);
    }
}
