using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using OpenSpecStore;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;

namespace TaskManager.Components;

/// <summary>
/// A page over the open repository. It reloads its data whenever the workspace changes (an edit here, or one from
/// outside), and runs module commands through <see cref="Run"/>, which shows a failure as a toast.
/// </summary>
public abstract class RepoPage : RtmComponent, IDisposable
{
    [Inject] protected Workspace Workspace { get; set; } = default!;
    [Inject] protected UiService Popups { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;
    [Inject] private IServiceProvider PageServices { get; set; } = default!;

    /// <summary>The clock the page's timers run on: the system's, unless a <see cref="TimeProvider"/> is registered (a test moves time by hand with one).</summary>
    protected TimeProvider Time => Ui.ClockOf(PageServices);

    protected override void OnInitialized() => Workspace.Changed += OnWorkspaceChanged;

    /// <summary>How many times <see cref="Load"/> has run: tests and the perf probe check that a navigation or a change reads once.</summary>
    public int Loads { get; private set; }

    private void LoadNow() { using IDisposable? scope = WindowScope(); lock (gate) loadedStamp = Stamp(); Loads++; Load(); }

    // What the repositories held when the page last read: a trailing reload that would read the same is skipped.
    private (string?, long, string) loadedStamp;

    /// <summary>
    /// What a read depends on: the store's version, and how far the start's load has come (headers in, git ready), which writes nothing to the store. Git becoming ready changes what the page
    /// shows (the commit filter, the uncommitted marks), so a change that only carries it must not look like the echo of what the page already read.
    /// </summary>
    private (string?, long, string) Stamp() => (OpenSpecStoreApi.RepoRoot, OpenSpecStoreApi.TotalVersion,
        string.Join('|', Workspace.Projects.Select(p => (Workspace.IsLoading(p) ? "L" : "") + (Workspace.IsGitReady(p) ? "G" : ""))));

    /// <summary>
    /// What the page's data depends on among its parameters (the item id, the document path). Blazor calls OnParametersSet again whenever the
    /// parent renders, whether or not a parameter changed; while this key stays the same the page does not read again: a change in the data
    /// reaches it through <see cref="Workspace.Changed"/>. Null (the default) reads on every call.
    /// </summary>
    protected virtual object? LoadKey => null;

    private object? loadedKey;
    private bool loaded;

    protected override void OnParametersSet()
    {
        object? key = LoadKey;
        if (loaded && key is not null && key.Equals(loadedKey)) return;
        (loaded, loadedKey) = (true, key);
        LoadNow();
    }

    /// <summary>Reads everything the page shows. Called on first render, when the page's <see cref="LoadKey"/> changes and on a workspace change; the repository is read here, never in the markup.</summary>
    protected abstract void Load();

    // Changed events that arrive close together (a write and the file watcher's echo, a command that writes twice) are one reload. The first is
    // read at once, so an edit shows without waiting; the ones inside the window after it become one more reload when the window ends.
    private const int CoalesceMs = 50;
    private readonly object gate = new();
    private ITimer? window;
    private bool windowOpen, dirty, disposed;

    // The store's stamp when this page's own background write finished, once the page showed that write's result itself: the Changed it raises is the page's own echo.
    private (string?, long, string)? ownStamp;

    protected virtual void OnWorkspaceChanged()
    {
        lock (gate)
        {
            if (disposed) return;
            if (ownStamp is { } own)   // one echo only: a later change, from outside or the same page, reads as usual
            {
                ownStamp = null;
                using (WindowScope()) if (Stamp() == own) { loadedStamp = own; return; }
            }
            if (windowOpen) { dirty = true; return; }
            windowOpen = true;
            window = Time.CreateTimer(_ => WindowEnded(), null, TimeSpan.FromMilliseconds(CoalesceMs), Timeout.InfiniteTimeSpan);
        }
        Reload();
    }

    /// <summary>
    /// The coalescing window's timer ended. It can be the timer's callback that is already running when the page closes (the window and its services go first): nothing here may throw into the timer,
    /// so a page that is disposed does nothing and any other failure is logged at Warning.
    /// </summary>
    internal void WindowEnded()
    {
        try
        {
            lock (gate) if (disposed) return;
            using IDisposable? scope = WindowScope();   // the stamp is of this window's project, like the one taken when the page read
            lock (gate)
            {
                if (disposed) return;
                // Nothing was written since the page read (the echo of its own write): another reload would only re-render an identical tree.
                if (!dirty || Stamp() == loadedStamp) { dirty = false; windowOpen = false; window?.Dispose(); window = null; return; }
                dirty = false;
                window?.Change(TimeSpan.FromMilliseconds(CoalesceMs), Timeout.InfiniteTimeSpan);
            }
            Reload();
        }
        catch (Exception e)
        {
            AppLog.Logger(GetType().Name).LogWarning(e, "The page's reload window ended with an error; the page was closed or its window is gone");
        }
    }

    private void Reload() => _ = InvokeAsync(() =>
    {
        if (disposed) return;
        LoadNow();
        StateHasChanged();
    });

    /// <summary>What a log line names when the call gives no target: the page's work item or note (<c>#wi123</c>, <c>#n7</c>), or nothing.</summary>
    protected virtual string? LogTarget => null;

    private static readonly Regex ModuleCall = new(@"\b[A-Z]\w*Api\.(\w+)\(", RegexOptions.Compiled);

    /// <summary>
    /// The action of a command: the module call in the expression the page passed (<c>WorkItemsApi.SetStatus(...)</c> gives SetStatus), else the name of the method that ran it.
    /// A command that is no change, only a refresh of what the page shows (<c>(true, "")</c>), is no action: null.
    /// </summary>
    internal static string? ActionOf(string? expression, string? member) =>
        expression is not null && expression.TrimStart().StartsWith("(true", StringComparison.Ordinal) ? null
        : ModuleCall.Match(expression ?? "") is { Success: true } m ? m.Groups[1].Value : member;

    /// <summary>
    /// One Information line for a user action that changed something: <c>ui &lt;page&gt; &lt;action&gt; &lt;#wiN | target&gt;</c>. Every command of a page passes here when it succeeds
    /// (<see cref="Run"/>, <see cref="RunInBackground"/>); a page that writes some other way calls it itself.
    /// </summary>
    protected void LogAction(string? action, string? target = null)
    {
        if (action is null) return;
        string name = GetType().Name, page = name.Length > 4 && name.EndsWith("Page", StringComparison.Ordinal) ? name[..^4] : name;
        UiLog.Action(page, action, target ?? LogTarget);
    }

    /// <summary>Runs a module command; a success refreshes every page through the workspace and logs the action, a failure is shown as a toast. <paramref name="target"/> names what it changed (the page's own item by default).</summary>
    protected bool Run((bool Success, string Message) result, string? target = null, [CallerArgumentExpression(nameof(result))] string? what = null, [CallerMemberName] string? member = null)
    {
        using Activity? command = AppLog.Start("ui.command", GetType().Name);
        if (Workspace.Do(result).Success) { command?.SetTag("result", "ok"); LogAction(ActionOf(what, member), target); Popups.Saved(); return true; }
        command?.SetTag("result", "refused: " + result.Message);
        AppLog.Logger(GetType().Name).LogWarning("A command was refused: {Message}", result.Message);
        Popups.Toast(result.Message, ToastKind.Warning);
        return false;
    }

    /// <summary>
    /// Runs a module command on the thread pool: the page draws at once (after <paramref name="show"/>, which puts the command's expected result into what the page shows),
    /// and the file write, its lock wait and the refresh of every page follow without holding the window. A failed command puts the page right by reading and shows a toast.
    /// With <paramref name="show"/> the page's own echo of the write is skipped: it already shows the result. Pass the command as a call (<c>() =&gt; Api.Do(...)</c>), not its result.
    /// </summary>
    protected async Task<bool> RunInBackground(Func<(bool Success, string Message)> command, Action? show = null, string? target = null, [CallerArgumentExpression(nameof(command))] string? what = null, [CallerMemberName] string? member = null)
    {
        using Activity? trace = AppLog.Start("ui.command", GetType().Name);   // the write and the scan that follow are its children: one trace id
        if (show is not null) { show(); StateHasChanged(); }
        (bool ok, string message) = await Workspace.DoLater(command, show is null ? null : NoteOwnWrite);
        trace?.SetTag("result", ok ? "ok" : "refused: " + message);
        if (ok) { LogAction(ActionOf(what, member), target); Popups.Saved(); return true; }
        AppLog.Logger(GetType().Name).LogWarning("A command was refused: {Message}", message);
        if (show is not null) Refresh();   // what was shown ahead of the write is put right by what is written
        Popups.Toast(message, ToastKind.Warning);
        return false;
    }

    /// <summary>Notes that the page showed the result of the write that has just finished itself: the Changed it raises (after the writes' quiet time, see <see cref="Workspace.NotifyWritten()"/>) is this page's own echo and reads nothing. Runs on the pool, after the write.</summary>
    protected void NoteOwnWrite()
    {
        using (WindowScope()) { (string?, long, string) stamp = Stamp(); lock (gate) ownStamp = stamp; }
    }

    /// <summary>Reads again and draws: for a page whose background command made more than <c>show</c> projected (its own echo was skipped).</summary>
    protected void Refresh() { LoadNow(); StateHasChanged(); }

    /// <summary>Like <see cref="Run"/>, for a command that saved or removed an attachment file: the connected storage's sync follows even when no board file moved.</summary>
    protected bool RunAttachment((bool Success, string Message) result, string? target = null, [CallerArgumentExpression(nameof(result))] string? what = null, [CallerMemberName] string? member = null)
    {
        bool ok = Run(result, target, what, member);
        if (ok) Workspace.AttachmentsChanged();
        return ok;
    }

    public virtual void Dispose()
    {
        Workspace.Changed -= OnWorkspaceChanged;
        lock (gate) { disposed = true; window?.Dispose(); window = null; }
    }
}
