namespace TaskManager.Services;

public enum ToastKind { Info, Success, Warning }

public sealed record Toast(int Id, string Text, ToastKind Kind);

/// <summary>A confirm or a one-line prompt; <see cref="Result"/> completes with the typed text / "yes", or null when cancelled.</summary>
public sealed record DialogRequest(string Title, string Text, string Yes, string No, bool IsPrompt, string Placeholder, TaskCompletionSource<string?> Result);

/// <summary>
/// App-wide dialogs and toasts, rendered by the Overlays component in the layout. Pages call
/// <see cref="Confirm"/>, <see cref="Prompt"/> and <see cref="Toast"/> and await the answer.
/// </summary>
public sealed class UiService
{
    private readonly List<Toast> toasts = [];
    private int nextToast;

    public event Action? Changed;

    public DialogRequest? Dialog { get; private set; }

    /// <summary>The message of the slow step running now (Overlays shows it with a spinner), or null.</summary>
    public string? BusyText { get; private set; }

    /// <summary>
    /// Runs a slow synchronous step (the OpenSpec CLI) behind a busy panel. The short delay lets the panel reach the
    /// screen first; its spinner is CSS, so it keeps turning while the step blocks .NET.
    /// </summary>
    public async Task<T> Busy<T>(string text, Func<T> work)
    {
        BusyText = text;
        Changed?.Invoke();
        await Task.Delay(50);   // ponytail: one render round trip; move work to Task.Run if steps ever need live progress
        try { return work(); }
        finally { BusyText = null; Changed?.Invoke(); }
    }

    public IReadOnlyList<Toast> Toasts { get { lock (toasts) return toasts.ToList(); } }

    public async Task<bool> Confirm(string title, string text, string yes = "OK", string no = "Cancel") =>
        await Show(new DialogRequest(title, text, yes, no, false, "", new())) is not null;

    public Task<string?> Prompt(string title, string placeholder, string action) =>
        Show(new DialogRequest(title, "", action, "Cancel", true, placeholder, new()));

    /// <summary>Closes the open dialog: a value means OK (the prompt text, or "yes" for a confirm), null means cancel.</summary>
    public void Close(string? result)
    {
        DialogRequest? d = Dialog;
        Dialog = null;
        Changed?.Invoke();
        d?.Result.TrySetResult(result);
    }

    private DateTime lastToast;

    /// <summary>
    /// "Saved", after any write to the repository. Skipped when a more specific toast just went up (Created, Merged, …),
    /// and never shown twice at once.
    /// </summary>
    public void Saved()
    {
        lock (toasts) if ((DateTime.UtcNow - lastToast).TotalMilliseconds < 600 || toasts.Any(t => t.Text == "Saved")) return;
        Toast("Saved", ToastKind.Success);
    }

    public void Toast(string text, ToastKind kind = ToastKind.Info)
    {
        Toast t;
        lock (toasts)
        {
            if (text != "Saved") toasts.RemoveAll(x => x.Text == "Saved");   // the specific message replaces the plain one
            lastToast = DateTime.UtcNow;
            toasts.Add(t = new Toast(++nextToast, text, kind));
        }
        Changed?.Invoke();
        _ = Task.Delay(3500).ContinueWith(_ =>
        {
            lock (toasts) toasts.Remove(t);
            Changed?.Invoke();
        }, TaskScheduler.Default);
    }

    private Task<string?> Show(DialogRequest d)
    {
        Dialog?.Result.TrySetResult(null);   // only one dialog at a time
        Dialog = d;
        Changed?.Invoke();
        return d.Result.Task;
    }
}
