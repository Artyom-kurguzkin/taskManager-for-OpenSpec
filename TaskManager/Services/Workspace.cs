using Features;
using GitHistory;
using OpenSpecStore;

namespace TaskManager.Services;

/// <summary>
/// Composition-root glue for the UI: opens a repository (configuring every module), and tells pages to
/// re-read when files change on disk or after the app's own edits.
/// </summary>
public sealed class Workspace : IDisposable
{
    private readonly SettingsStore settings;

    public Workspace(SettingsStore settings)
    {
        this.settings = settings;
        OpenSpecStoreApi.Changed += Notify;
    }

    /// <summary>Raised on any change; components re-read through the module Apis and re-render.</summary>
    public event Action? Changed;

    public string? Root => OpenSpecStoreApi.RepoRoot;

    public (bool Success, string Message) Open(string root)
    {
        (bool ok, string message) = OpenSpecStoreApi.Open(root);
        if (!ok) return (ok, message);
        GitHistoryApi.Configure(root);
        settings.Update(s => s with { LastRepo = OpenSpecStoreApi.RepoRoot });
        Notify();
        return (true, "");
    }

    /// <summary>Who the app writes as: the name in Settings, else git user.name, else the OS user.</summary>
    public string Me => settings.Current.Name ?? GitHistoryApi.UserName() ?? Environment.UserName;

    public IReadOnlyDictionary<string, string> FeatureNames => FeaturesApi.GetAll().ToDictionary(f => f.Path, f => f.Name);

    /// <summary>Runs a module command and refreshes the UI when it succeeds.</summary>
    public (bool Success, string Message) Do((bool Success, string Message) result)
    {
        if (result.Success) Notify();
        return result;
    }

    public void Notify() => Changed?.Invoke();

    public void Dispose() => OpenSpecStoreApi.Changed -= Notify;
}
