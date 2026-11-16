using Microsoft.AspNetCore.Components;
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

    protected override void OnInitialized() => Workspace.Changed += OnChanged;

    /// <summary>How many times <see cref="Load"/> has run: tests and the perf probe check that a navigation or a change reads once.</summary>
    public int Loads { get; private set; }

    private void LoadNow() { using IDisposable? scope = WindowScope(); lock (gate) loadedStamp = Stamp(); Loads++; Load(); }

    // What the repositories held when the page last read: a trailing reload that would read the same is skipped.
    private (string?, long) loadedStamp;
    private static (string?, long) Stamp() => (OpenSpecStoreApi.RepoRoot, OpenSpecStoreApi.TotalVersion);

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

    protected virtual void OnChanged()
    {
        lock (gate)
        {
            if (disposed) return;
            if (windowOpen) { dirty = true; return; }
            windowOpen = true;
            window = Time.CreateTimer(_ => WindowEnded(), null, TimeSpan.FromMilliseconds(CoalesceMs), Timeout.InfiniteTimeSpan);
        }
        Reload();
    }

    private void WindowEnded()
    {
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

    private void Reload() => _ = InvokeAsync(() =>
    {
        if (disposed) return;
        LoadNow();
        StateHasChanged();
    });

    /// <summary>Runs a module command; a success refreshes every page through the workspace, a failure is shown as a toast.</summary>
    protected bool Run((bool Success, string Message) result)
    {
        if (Workspace.Do(result).Success) { Popups.Saved(); return true; }
        Popups.Toast(result.Message, ToastKind.Warning);
        return false;
    }

    /// <summary>Like <see cref="Run"/>, for a command that saved or removed an attachment file: the connected storage's sync follows even when no board file moved.</summary>
    protected bool RunAttachment((bool Success, string Message) result)
    {
        bool ok = Run(result);
        if (ok) Workspace.AttachmentsChanged();
        return ok;
    }

    public virtual void Dispose()
    {
        Workspace.Changed -= OnChanged;
        lock (gate) { disposed = true; window?.Dispose(); window = null; }
    }
}
