using TaskManager.Board;
using TaskManager.Services;

namespace TaskManager.Tests;

public sealed class RememberingSettingsTests
{
    private readonly string file = Path.Combine(Path.GetTempPath(), "rtm-settings-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact(DisplayName = "Settings saved now are there the next time the app starts")]
    public void SaveAndReload()
    {
        SettingsStore store = new(file);
        store.Update(s => s with { Theme = "dark", LastRepo = @"C:\repo", Name = "Artyom", Hints = true });
        store.SaveBoard(@"C:\repo", new BoardOptions(GroupBy: "feature", Category: "Functional", View: "list"));

        SettingsStore reopened = new(file);

        Assert.Equal(("dark", @"C:\repo", "Artyom", true), (reopened.Current.Theme, reopened.Current.LastRepo, reopened.Current.Name, reopened.Current.Hints));
        Assert.Equal(new BoardOptions(GroupBy: "feature", Category: "Functional", View: "list", ListGroupBy: "feature", ListSwimlane: "none"), reopened.Board(@"C:\repo"));   // a list saved before it had its own groups keeps its grouping
        File.Delete(file);
    }

    [Fact(DisplayName = "A board grouped by the old name \"bucket\" (it is called feature again) still loads, as \"feature\"")]
    public void LegacyFeatureGroupByMigrates()
    {
        SettingsStore store = new(file);
        store.SaveBoard(@"C:\repo", new BoardOptions(GroupBy: "bucket"));

        Assert.Equal("feature", store.Board(@"C:\repo").GroupBy);
        File.Delete(file);
    }

    [Fact(DisplayName = "A saved list keeps the groups it had, a saved board or a fresh repository gets a flat list, and a choice made for the list is kept apart from the board's columns")]
    public void ListGroupsDefaultToNone()
    {
        SettingsStore store = new(file);
        Assert.Equal(("none", "none"), (store.Board(@"C:\fresh").ListGroupBy, store.Board(@"C:\fresh").ListSwimlane));

        store.SaveBoard(@"C:\board", new BoardOptions(GroupBy: "feature", Swimlane: "label"));
        Assert.Equal(("none", "none"), (store.Board(@"C:\board").ListGroupBy, store.Board(@"C:\board").ListSwimlane));

        store.SaveBoard(@"C:\list", new BoardOptions(GroupBy: "label", Swimlane: "priority", View: "list"));
        Assert.Equal(("label", "priority"), (store.Board(@"C:\list").ListGroupBy, store.Board(@"C:\list").ListSwimlane));

        store.SaveBoard(@"C:\list", store.Board(@"C:\list") with { ListGroupBy = "none" });
        SettingsStore reopened = new(file);
        Assert.Equal(("none", "priority", "label"), (reopened.Board(@"C:\list").ListGroupBy, reopened.Board(@"C:\list").ListSwimlane, reopened.Board(@"C:\list").GroupBy));
        File.Delete(file);
    }

    [Fact(DisplayName = "Which repositories are open as tabs is remembered between sessions")]
    public void OpenReposRoundTrip()
    {
        SettingsStore store = new(file);
        store.Update(s => s with { OpenRepos = [@"C:\repo1", @"C:\repo2"] });

        SettingsStore reopened = new(file);

        Assert.Equal([@"C:\repo1", @"C:\repo2"], reopened.Current.OpenRepos);
        File.Delete(file);
    }

    [Fact(DisplayName = "The plans folder is a per-repository setting: DevResources/plans unless changed, remembered between sessions, other repositories unaffected")]
    public void PlansFolderPerRepo()
    {
        string file = Path.Combine(Path.GetTempPath(), "rtm-" + Guid.NewGuid().ToString("N") + ".json");
        SettingsStore store = new(file);
        Assert.Equal("DevResources/plans", store.PlansDir(@"C:\repo-a"));

        store.SetPlansDir(@"C:\repo-a", @"local\plans\");
        Assert.Equal("local/plans", store.PlansDir(@"C:\repo-a"));   // forward slashes, no trailing slash: it is written into a markdown guide
        Assert.Equal("DevResources/plans", store.PlansDir(@"C:\repo-b"));
        Assert.Equal("local/plans", new SettingsStore(file).PlansDir(@"C:\repo-a"));

        store.SetPlansDir(@"C:\repo-a", " ");   // empty = back to the default
        Assert.Equal("DevResources/plans", store.PlansDir(@"C:\repo-a"));
        File.Delete(file);
    }

    [Fact(DisplayName = "A missing or unreadable settings file starts with defaults")]
    public void Defaults()
    {
        File.WriteAllText(file, "{ not json");
        SettingsStore store = new(file);
        Assert.Equal("light", store.Current.Theme);
        Assert.Null(store.Current.LastRepo);
        Assert.Equal(new BoardOptions(ListGroupBy: "none", ListSwimlane: "none"), store.Board(@"C:\other"));   // the board starts as status columns, the list flat
        File.Delete(file);
    }

    [Fact(DisplayName = "A board's merged status columns are saved with the board, per repository, and come back")]
    public void MergesAreSavedPerBoard()
    {
        SettingsStore store = new(file);
        store.SaveBoard(@"C:\a", BoardModel.Merge(new BoardOptions(), ["Completed", "completed"], "Completed"));
        store.SaveBoard(@"C:\b", new BoardOptions());

        SettingsStore reopened = new(file);

        StatusMerge merge = Assert.Single(reopened.Board(@"C:\a").Merges!);
        Assert.Equal("Completed", merge.Name);
        Assert.Equal(["Completed", "completed"], merge.Statuses);
        Assert.Null(reopened.Board(@"C:\b").Merges);
        File.Delete(file);
    }
}
