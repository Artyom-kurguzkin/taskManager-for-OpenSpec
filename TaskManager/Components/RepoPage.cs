using Microsoft.AspNetCore.Components;
using TaskManager.Services;

namespace TaskManager.Components;

/// <summary>
/// A page over the open repository. It reloads its data whenever the workspace changes (an edit here, or one from
/// outside), and runs module commands through <see cref="Run"/>, which shows a failure as a toast.
/// </summary>
public abstract class RepoPage : ComponentBase, IDisposable
{
    [Inject] protected Workspace Workspace { get; set; } = default!;
    [Inject] protected UiService Popups { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;

    protected override void OnInitialized() => Workspace.Changed += OnChanged;

    protected override void OnParametersSet() => Load();

    /// <summary>Reads everything the page shows. Called on first render, on every parameter change and on every workspace change.</summary>
    protected abstract void Load();

    protected virtual void OnChanged() => InvokeAsync(() => { Load(); StateHasChanged(); });

    /// <summary>Runs a module command; a success refreshes every page through the workspace, a failure is shown as a toast.</summary>
    protected bool Run((bool Success, string Message) result)
    {
        if (Workspace.Do(result).Success) { Popups.Saved(); return true; }
        Popups.Toast(result.Message, ToastKind.Warning);
        return false;
    }

    public virtual void Dispose() => Workspace.Changed -= OnChanged;
}
