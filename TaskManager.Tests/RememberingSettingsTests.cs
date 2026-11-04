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
        Assert.Equal(new BoardOptions(GroupBy: "feature", Category: "Functional", View: "list"), reopened.Board(@"C:\repo"));
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
        Assert.Equal(new BoardOptions(), store.Board(@"C:\other"));
        File.Delete(file);
    }

    [Fact(DisplayName = "A new repository opens with the default board view from settings")]
    public void DefaultViewForNewRepo()
    {
        SettingsStore store = new(file);
        store.Update(s => s with { DefaultView = "list" });
        Assert.Equal("list", store.Board(@"C:\new").View);
        File.Delete(file);
    }
}
