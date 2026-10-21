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
        store.Update(s => s with { Theme = "dark", LastRepo = @"C:\repo", Name = "Artyom" });
        store.SaveBoard(@"C:\repo", new BoardOptions(GroupBy: "bucket", Category: "Functional", View: "list"));

        SettingsStore reopened = new(file);

        Assert.Equal(("dark", @"C:\repo", "Artyom"), (reopened.Current.Theme, reopened.Current.LastRepo, reopened.Current.Name));
        Assert.Equal(new BoardOptions(GroupBy: "bucket", Category: "Functional", View: "list"), reopened.Board(@"C:\repo"));
        File.Delete(file);
    }

    [Fact(DisplayName = "A board grouped by the old name \"feature\" (renamed to \"bucket\") still loads, as \"bucket\"")]
    public void LegacyFeatureGroupByMigrates()
    {
        SettingsStore store = new(file);
        store.SaveBoard(@"C:\repo", new BoardOptions(GroupBy: "feature"));

        Assert.Equal("bucket", store.Board(@"C:\repo").GroupBy);
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
