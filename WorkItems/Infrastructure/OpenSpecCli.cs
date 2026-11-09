using System.Diagnostics;
using System.Text.Json;
using OpenSpecStore;

namespace WorkItems.Infrastructure;

/// <summary>
/// Archives a change exactly as the CLI does it, so delta specs merge into openspec/specs the official way. Flags let the caller
/// retry a refusal with <c>--no-validate</c> and <c>--skip-specs</c>.
/// </summary>
internal static class OpenSpecCli
{
    // ponytail: pinned CLI version via npx; prefer a global `openspec` once teams install it.
    internal const string Version = "1.14.3";
    private const string Npx = "npx -y @fission-ai/openspec@" + Version;

    /// <summary>
    /// <c>openspec archive &lt;folder&gt; --yes</c> plus <paramref name="flags"/>. CouldRun is false when the CLI never got to decide
    /// (no repository, no Node.js, timeout): retrying with other flags would not help, so the message goes to the user as it is.
    /// </summary>
    internal static (bool Success, string Message, bool CouldRun) Archive(string folder, IReadOnlyList<string> flags)
    {
        if (OpenSpecStoreApi.RepoRoot is not string root) return (false, "No repository is open", false);
        // npx spends ~0.85 s resolving the package before the CLI's own ~1.3 s; node on npx's cached copy skips that.
        ProcessStartInfo psi;
        if (FindCached(NpmCache()) is string entry)
        {
            psi = new("node");
            foreach (string a in new[] { entry, "archive", folder, "--yes" }.Concat(flags)) psi.ArgumentList.Add(a);
        }
        else
        {
            string command = $"{Npx} archive {folder} --yes {string.Join(' ', flags)}";
            psi = OperatingSystem.IsWindows() ? new("cmd.exe", $"/c {command}") : new("/bin/sh", $"-c \"{command}\"");
        }
        psi.WorkingDirectory = root;
        psi.RedirectStandardOutput = psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = psi.StandardErrorEncoding = System.Text.Encoding.UTF8;   // the CLI prints ⚠ and ✗; the console code page would garble them
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        try
        {
            using Process p = Process.Start(psi)!;
            Task<string> err = p.StandardError.ReadToEndAsync();
            string output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(TimeSpan.FromMinutes(2))) { p.Kill(true); return (false, "openspec archive timed out", false); }
            return p.ExitCode == 0 ? (true, "", true) : (false, (output + err.Result).Trim(), true);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (false, "Couldn't run the OpenSpec CLI (needs Node.js/npx on PATH): " + e.Message, false);
        }
    }

    /// <summary><c>openspec validate &lt;folder&gt; --strict</c> in the repository root. Message is the CLI's output (or why it could not run).</summary>
    internal static (bool Success, string Message) Validate(string folder)
    {
        if (OpenSpecStoreApi.RepoRoot is not string root) return (false, "No repository is open");
        ProcessStartInfo psi;
        if (FindCached(NpmCache()) is string entry)
        {
            psi = new("node");
            foreach (string a in new[] { entry, "validate", folder, "--strict" }) psi.ArgumentList.Add(a);
        }
        else
        {
            string command = $"{Npx} validate {folder} --strict";
            psi = OperatingSystem.IsWindows() ? new("cmd.exe", $"/c {command}") : new("/bin/sh", $"-c \"{command}\"");
        }
        psi.WorkingDirectory = root;
        psi.RedirectStandardOutput = psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = psi.StandardErrorEncoding = System.Text.Encoding.UTF8;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        try
        {
            using Process p = Process.Start(psi)!;
            Task<string> err = p.StandardError.ReadToEndAsync();
            string output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(TimeSpan.FromMinutes(2))) { p.Kill(true); return (false, "openspec validate timed out"); }
            return (p.ExitCode == 0, (output + err.Result).Trim());
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (false, "Couldn't run the OpenSpec CLI (needs Node.js/npx on PATH): " + e.Message);
        }
    }

    /// <summary>npm's cache folder: <c>npm_config_cache</c> if set, else npm's default for this OS.</summary>
    private static string NpmCache() =>
        Environment.GetEnvironmentVariable("npm_config_cache") is { Length: > 0 } set ? set
        : OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "npm-cache")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".npm");

    /// <summary>The CLI script npx cached under <paramref name="cache"/>/_npx at the pinned <see cref="Version"/>, or null.</summary>
    internal static string? FindCached(string cache)
    {
        string npx = Path.Combine(cache, "_npx");
        if (!Directory.Exists(npx)) return null;
        foreach (string dir in Directory.EnumerateDirectories(npx))
        {
            string pkg = Path.Combine(dir, "node_modules", "@fission-ai", "openspec");
            try
            {
                using JsonDocument json = JsonDocument.Parse(File.ReadAllText(Path.Combine(pkg, "package.json")));
                if (json.RootElement.GetProperty("version").GetString() != Version) continue;
                string script = Path.Combine(pkg, json.RootElement.GetProperty("bin").GetProperty("openspec").GetString()!);
                if (File.Exists(script)) return Path.GetFullPath(script);
            }
            catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { }
        }
        return null;
    }
}
