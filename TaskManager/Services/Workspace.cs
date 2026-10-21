using Features;
using GitHistory;
using OpenSpecStore;
using WorkItems;

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
        WorkItemsApi.ConfigureContributors(() => GitHistoryApi.IsRepository() ? (GitHistoryApi.HeadToken(), GitHistoryApi.AuthorsOfChange) : null);
        settings.Update(s => s with { LastRepo = OpenSpecStoreApi.RepoRoot });
        Notify();
        return (true, "");
    }

    /// <summary>Repositories open as tabs; falls back to just the active one once settings have any value at all.</summary>
    public IReadOnlyList<string> OpenRepos =>
        settings.Current.OpenRepos is { Count: > 0 } list ? list : (Root is string r ? [r] : []);

    /// <summary>Opens a repository as a new tab (or switches to it if already open) and makes it active.</summary>
    public (bool Success, string Message) OpenTab(string root)
    {
        List<string> before = OpenRepos.ToList();   // snapshot before Open() switches Root, which OpenRepos falls back on
        (bool ok, string message) = Open(root);
        if (ok) settings.Update(s => s with { OpenRepos = [.. before.Where(p => SettingsStore.Key(p) != SettingsStore.Key(root)), root] });
        return (ok, message);
    }

    /// <summary>Closes a tab; the last remaining one can't be closed. Switches to another tab if the active one closes.</summary>
    public void CloseTab(string repo)
    {
        if (OpenRepos.Count <= 1) return;
        List<string> remaining = OpenRepos.Where(p => SettingsStore.Key(p) != SettingsStore.Key(repo)).ToList();
        settings.Update(s => s with { OpenRepos = remaining });
        if (Root is string r && SettingsStore.Key(r) == SettingsStore.Key(repo)) Open(remaining[^1]);
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

    /// <summary>Creates "New work item" (assigned to me unless <paramref name="who"/> says otherwise) and refreshes every view.</summary>
    public (bool Ok, string Message, int Id) CreateItem(string status = "Backlog", string[]? features = null, string[]? who = null)
    {
        (bool ok, string message, int id) = WorkItemsApi.Create("New work item", status, features, who ?? [Me]);
        if (ok) Notify();
        return (ok, message, id);
    }

    public void Dispose() => OpenSpecStoreApi.Changed -= Notify;
}
