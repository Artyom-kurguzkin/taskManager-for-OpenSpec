using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TaskManager.AppState;

namespace TaskManager.Helpers;

public enum UpdateState { NotChecked, Checking, UpToDate, Available, Downloading, Ready, Failed }

/// <summary>
/// What the update check needs to know about this run. <paramref name="Version"/> is the app's own version (null = unknown), <paramref name="ExePath"/> the exe that is running, <paramref name="IsDebug"/> a Debug build,
/// <paramref name="UpdatesFolder"/> where releases are staged, <paramref name="Endpoint"/> the "latest release" address and <paramref name="Timeout"/> how long one request may take.
/// </summary>
public sealed record UpdateSetup(string? Version, string ExePath, bool IsDebug, string UpdatesFolder, Uri Endpoint, TimeSpan Timeout)
{
    public const string Repository = "Artyom-kurguzkin/taskManager-for-OpenSpec";
    public const string AssetName = "RepoTaskManager-win-x64.zip";

    /// <summary>RTM_UPDATE_URL points the check at another address (a demo or a test of the real app against a local fake); otherwise the repository's latest release on GitHub.</summary>
    public static Uri DefaultEndpoint => Uri.TryCreate(Environment.GetEnvironmentVariable("RTM_UPDATE_URL"), UriKind.Absolute, out Uri? own) ? own : new($"https://api.github.com/repos/{Repository}/releases/latest");

    /// <summary>This run: the version Nerdbank.GitVersioning stamped on the assembly (AssemblyInformationalVersion; absent until the build stamps one), the running exe, the configuration, and the app's own folder.</summary>
    public static UpdateSetup ForThisRun()
    {
        string? version = typeof(UpdateSetup).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
#if DEBUG
        const bool debug = true;
#else
        const bool debug = false;
#endif
        return new(version, Environment.ProcessPath ?? "", debug, Path.Combine(Paths.Home, "updates"), DefaultEndpoint, TimeSpan.FromSeconds(10));
    }
}

/// <summary>A version as the check compares it: the numbers, and the prerelease suffix (a build between releases has one: 0.1.5-g3f2a1b9 comes before 0.1.5).</summary>
public sealed record SemVer(Version Numbers, string? Prerelease) : IComparable<SemVer>
{
    private static readonly Regex Shape = new(@"^v?(?<n>\d+(\.\d+){0,3})(-(?<pre>[0-9A-Za-z.\-]+))?(\+.*)?$", RegexOptions.CultureInvariant);

    public static SemVer? Parse(string? text)
    {
        if (text is null || Shape.Match(text.Trim()) is not { Success: true } m) return null;
        string[] parts = m.Groups["n"].Value.Split('.');
        int[] n = new int[4];
        for (int i = 0; i < parts.Length; i++) if (!int.TryParse(parts[i], out n[i])) return null;
        return new(new Version(n[0], n[1], n[2], n[3]), m.Groups["pre"].Success ? m.Groups["pre"].Value : null);
    }

    /// <summary>The version as shown: no leading v, no +commit.</summary>
    public override string ToString()
    {
        string numbers = Numbers.Revision > 0 ? Numbers.ToString(4) : Numbers.ToString(3);
        return Prerelease is null ? numbers : numbers + "-" + Prerelease;
    }

    public int CompareTo(SemVer? other)
    {
        if (other is null) return 1;
        if (Numbers.CompareTo(other.Numbers) is int c and not 0) return c;
        if (Prerelease is null || other.Prerelease is null) return (Prerelease is null ? 1 : 0) - (other.Prerelease is null ? 1 : 0);   // the release is after its own prereleases
        return string.CompareOrdinal(Prerelease, other.Prerelease);
    }
}

/// <summary>
/// Keeps the app up to date. On start (and on "Check now" in Settings) it asks GitHub for the repository's latest release; when that is newer and this run is a release copy, it downloads the zip, checks it
/// (the published checksum, and that it holds TaskManager.exe), stages it under the updates folder and offers a restart. A restart, or the app's exit after "Later", starts <see cref="UpdateHelper"/>, which
/// swaps the files once this process has ended. A development build (Debug, no or a non-public version, or an exe under a project's bin/) asks and reports but never installs. Nothing here throws:
/// a check that cannot be made is a state with a reason, and a line in the log.
/// </summary>
public sealed class AppUpdates
{
    private readonly HttpClient http;
    private readonly UiService ui;
    private readonly UpdateSetup setup;
    private readonly Func<UpdateApplication, bool> launch;
    private readonly ILogger log = AppLog.Logger("Update");
    private readonly SemaphoreSlim busy = new(1, 1);
    private readonly SemVer? own;
    private string? staged;
    private bool applyOnExit;

    public AppUpdates(HttpClient http, UiService ui, UpdateSetup setup, Func<UpdateApplication, bool>? launch = null)
    {
        (this.http, this.ui, this.setup) = (http, ui, setup);
        this.launch = launch ?? (apply => UpdateHelper.Launch(apply, Path.Combine(setup.UpdatesFolder, "apply.log")) is not null);
        own = SemVer.Parse(setup.Version);
        WhyNot = Decide(own, setup);
        log.LogInformation("{Label}; {Updating}", VersionLabel, WhyNot is null ? "this copy updates itself" : "it does not update itself: " + WhyNot);
    }

    public UpdateState State { get; private set; }
    /// <summary>The newest release found (without its v), or null before a check finds one.</summary>
    public string? Latest { get; private set; }
    private string? failure;

    /// <summary>Raised when the state changes (the page shows it). It can come from a pool thread.</summary>
    public event Action? Changed;
    /// <summary>Raised when the user chose "Restart now" and the helper is waiting: the window should close.</summary>
    public event Action? RestartRequested;

    /// <summary>The app's version without the +commit part, or "unknown" when the build carries none.</summary>
    public string Current => own?.ToString() ?? "unknown";
    public bool CanUpdate => WhyNot is null;
    /// <summary>Why this copy never updates itself, or null when it does.</summary>
    public string? WhyNot { get; }
    public string VersionLabel => $"Version {Current}" + (CanUpdate ? "" : " (development build)");

    public string Summary => State switch
    {
        UpdateState.NotChecked => "Not checked yet",
        UpdateState.Checking => "Checking…",
        UpdateState.UpToDate => "Up to date",
        UpdateState.Available => $"{Latest} is available",
        UpdateState.Downloading => $"{Latest} is available — updating",
        UpdateState.Ready => applyOnExit ? $"{Latest} is downloaded and installs when you close the app" : $"{Latest} is downloaded; restart to use it",
        _ => $"Couldn't check ({failure})",
    };

    /// <summary>A copy that updates itself is a release one: not Debug, a public version (no prerelease suffix) and an exe that is not under a project's bin/ or obj/.</summary>
    private static string? Decide(SemVer? version, UpdateSetup setup)
    {
        if (setup.IsDebug) return "this is a Debug build";
        if (version is null) return "its version is unknown";
        if (version.Prerelease is not null) return "its version is not a public release";
        if (!string.Equals(Path.GetFileName(setup.ExePath), "TaskManager.exe", StringComparison.OrdinalIgnoreCase)) return "it is not running from TaskManager.exe";
        if (Regex.IsMatch(setup.ExePath, @"[\\/](bin[\\/](Debug|Release)|obj)[\\/]", RegexOptions.IgnoreCase)) return "it runs from a project's build folder, not a release folder";
        return null;
    }

    private void Set(UpdateState state, string? why = null)
    {
        (State, failure) = (state, why);
        Changed?.Invoke();
    }

    /// <summary>
    /// Checks for a newer release and, when this copy updates itself, downloads it and asks whether to restart. Returns when that is settled (the answer to the offer is awaited). A second call while
    /// one runs returns at once.
    /// </summary>
    public async Task CheckAsync(CancellationToken cancel = default)
    {
        if (!busy.Wait(0)) return;
        bool offer = false;
        try
        {
            offer = await CheckAndStageAsync(cancel);
        }
        catch (Exception e)
        {
            log.LogError(e, "The update failed unexpectedly");
            Set(UpdateState.Failed, e.Message);
        }
        finally { busy.Release(); }
        if (offer) await OfferAsync();
    }

    private async Task<bool> CheckAndStageAsync(CancellationToken cancel)
    {
        Set(UpdateState.Checking);
        log.LogInformation("Checking {Endpoint} for a newer release than {Current}", setup.Endpoint, Current);
        JsonElement release;
        try
        {
            using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            limit.CancelAfter(setup.Timeout);
            using HttpRequestMessage ask = new(HttpMethod.Get, setup.Endpoint);
            ask.Headers.UserAgent.ParseAdd("RepoTaskManager");
            ask.Headers.Accept.ParseAdd("application/vnd.github+json");
            using HttpResponseMessage answer = await http.SendAsync(ask, limit.Token);
            if (!answer.IsSuccessStatusCode) throw new HttpRequestException($"GitHub answered {(int)answer.StatusCode} {answer.ReasonPhrase}");
            release = JsonDocument.Parse(await answer.Content.ReadAsStringAsync(limit.Token)).RootElement.Clone();
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException)
        {
            string why = e is OperationCanceledException ? $"no answer in {setup.Timeout.TotalSeconds:0.#} seconds" : e.Message;
            log.LogWarning("Could not check for an update: {Why}", why);
            Set(UpdateState.Failed, why);
            return false;
        }

        SemVer? latest = SemVer.Parse(release.TryGetProperty("tag_name", out JsonElement tag) ? tag.GetString() : null);
        if (latest is null) { log.LogWarning("Could not check for an update: the release has no usable tag"); Set(UpdateState.Failed, "the latest release has no version tag"); return false; }
        Latest = latest.ToString();
        if (own is not null && latest.CompareTo(own) <= 0)
        {
            log.LogInformation("Up to date: running {Current}, the latest release is {Latest}", Current, Latest);
            Set(UpdateState.UpToDate);
            return false;
        }
        log.LogInformation("A newer release is available: {Latest} (running {Current})", Latest, Current);
        if (!CanUpdate) { log.LogInformation("Not updating: {Why}", WhyNot); Set(UpdateState.Available); return false; }

        Set(UpdateState.Downloading);
        ui.Toast($"Updating to v{Latest}…");
        try { staged = await StageAsync(release, Latest, cancel); }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException)
        {
            log.LogWarning("Could not download v{Latest}: {Why}", Latest, e.Message);
            Set(UpdateState.Failed, e.Message);
            ui.Toast($"Could not update to v{Latest}: {e.Message}", ToastKind.Warning);
            return false;
        }
        Set(UpdateState.Ready);
        return true;
    }

    /// <summary>Downloads the release's zip into updates/v{version}/, checks and unpacks it into files/ there. A folder already unpacked and checked (a "Later" that was never installed) is reused.</summary>
    private async Task<string> StageAsync(JsonElement release, string version, CancellationToken cancel)
    {
        string folder = Path.Combine(setup.UpdatesFolder, "v" + version), files = Path.Combine(folder, "files"), ready = Path.Combine(folder, "ready");
        if (File.Exists(ready) && File.Exists(Path.Combine(files, "TaskManager.exe"))) { log.LogInformation("v{Latest} is already downloaded in {Folder}", version, folder); return files; }

        JsonElement asset = release.TryGetProperty("assets", out JsonElement all) && all.ValueKind == JsonValueKind.Array
            ? all.EnumerateArray().FirstOrDefault(a => a.TryGetProperty("name", out JsonElement n) && n.GetString() == UpdateSetup.AssetName) : default;
        if (asset.ValueKind != JsonValueKind.Object || !asset.TryGetProperty("browser_download_url", out JsonElement url) || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out Uri? address))
            throw new InvalidOperationException($"the release has no {UpdateSetup.AssetName}");

        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            Directory.CreateDirectory(folder);
            string zip = Path.Combine(folder, UpdateSetup.AssetName);
            log.LogInformation("Downloading {Url} to {Zip}", address, zip);
            using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            limit.CancelAfter(TimeSpan.FromMinutes(10));
            using (HttpRequestMessage ask = new(HttpMethod.Get, address))
            {
                ask.Headers.UserAgent.ParseAdd("RepoTaskManager");
                using HttpResponseMessage answer = await http.SendAsync(ask, HttpCompletionOption.ResponseHeadersRead, limit.Token);
                if (!answer.IsSuccessStatusCode) throw new HttpRequestException($"the download answered {(int)answer.StatusCode} {answer.ReasonPhrase}");
                await using FileStream target = File.Create(zip);
                await answer.Content.CopyToAsync(target, limit.Token);
            }

            if (asset.TryGetProperty("digest", out JsonElement digest) && digest.GetString() is { } published && published.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                await using FileStream read = File.OpenRead(zip);
                string actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(read, limit.Token));
                if (!string.Equals(actual, published[7..], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("the download does not match its published checksum");
                log.LogInformation("The download matches its published checksum");
            }
            try { ZipFile.ExtractToDirectory(zip, files); }
            catch (InvalidDataException) { throw new InvalidDataException("the download is not a zip file"); }
            if (!File.Exists(Path.Combine(files, "TaskManager.exe"))) throw new InvalidDataException("the release does not hold TaskManager.exe");
            File.WriteAllText(ready, version);
            File.Delete(zip);
            foreach (string old in Directory.GetDirectories(setup.UpdatesFolder, "v*").Where(d => d != folder)) TryDelete(old);   // earlier downloads
            log.LogInformation("v{Latest} is downloaded, checked and unpacked in {Files}", version, files);
            return files;
        }
        catch
        {
            TryDelete(folder);   // a refused download leaves nothing behind
            throw;
        }
    }

    private void TryDelete(string folder)
    {
        try { Directory.Delete(folder, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { log.LogWarning("Could not remove {Folder}: {Why}", folder, e.Message); }
    }

    /// <summary>The popup: restart now (the helper starts and the window closes) or later (the swap happens when the app exits).</summary>
    private async Task OfferAsync()
    {
        bool now = await ui.Confirm($"Update to v{Latest} is ready", $"Repo Tasks v{Latest} is downloaded (this is {Current}). Restart now to use it, or it is installed when you close the app.", yes: "Restart now", no: "Later");
        if (now)
        {
            log.LogInformation("Restarting now to install v{Latest}", Latest);
            if (Apply(restart: true)) RestartRequested?.Invoke();
            else { ui.Toast("Could not start the update; it will be tried when you close the app.", ToastKind.Warning); applyOnExit = true; }
        }
        else
        {
            log.LogInformation("Later chosen: v{Latest} is installed when the app exits", Latest);
            applyOnExit = true;
        }
        Changed?.Invoke();
    }

    private bool applied;

    private bool Apply(bool restart)
    {
        if (staged is null || applied) return false;
        applied = launch(new UpdateApplication(staged, Path.GetDirectoryName(setup.ExePath)!, Path.Combine(setup.UpdatesFolder, "previous"), Environment.ProcessId, restart ? setup.ExePath : null));
        return applied;
    }

    /// <summary>Called as the app exits: a release downloaded and not yet installed ("Later") is installed now, by the helper, once this process has ended.</summary>
    public void ApplyOnExit()
    {
        if (!applyOnExit) return;
        log.LogInformation("Installing v{Latest} as the app exits", Latest);
        Apply(restart: false);
    }
}
