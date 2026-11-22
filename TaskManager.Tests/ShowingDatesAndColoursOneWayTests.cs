using System.Globalization;
using System.Text.RegularExpressions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Radzen;
using DateRange = TaskManager.Components.Shared.DateRange;
using TaskManager.Components.Pages;
using TaskManager.Components;
using TaskManager.Components.Layout;
using TaskManager.Components.Shared;
using TaskManager.AppState;
using TaskManager.BackgroundJobs;
using TaskManager.Helpers;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Tests;

/// <summary>Dates, periods and colours are written by Ui.cs alone, on the app's one clock: every screen shows a value the same way.</summary>
public sealed class ShowingDatesAndColoursOneWayTests : IDisposable
{
    private readonly TestRepo repo = new();
    private readonly string settingsFile = Path.Combine(Path.GetTempPath(), "rtm-dc-" + Guid.NewGuid().ToString("N") + ".json");
    private IRenderedComponent<SideNav>? nav;

    public void Dispose()
    {
        repo.Dispose();
        try { File.Delete(settingsFile); } catch (IOException) { }
    }

    private BunitContext NewContext(TimeProvider? time = null)
    {
        BunitContext ctx = new();
        if (time is not null) ctx.Services.AddSingleton(time);   // the app's clock: the test moves it by hand
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<UiService>();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(new SettingsStore(settingsFile));
        ctx.Services.AddSingleton<Workspace>();
        Assert.True(ctx.Services.GetRequiredService<Workspace>().Open(repo.Root).Success);
        ctx.Render<Overlays>();
        nav = ctx.Render<SideNav>();
        return ctx;
    }

    private void ShowList()
    {
        nav!.ClickWhenReady("button[aria-label=Layout]");
        nav!.FireWhenReady(".menu-item", e => e.Click(), all => all.Single(i => i.TextContent.Trim() == "List"));
    }

    [Fact(DisplayName = "A date is written by one table of formats: the card, the list, the item page and the examine panel each show it as Ui says, in the user's culture")]
    public async Task ADateShowsOneWay()
    {
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");   // a culture where "d MMM" and "d MMM yyyy" read differently from the invariant one
        await using BunitContext ctx = NewContext();
        DateOnly started = new(2026, 11, 9), due = new(2026, 11, 16);
        WorkItemsApi.SetStarted(123, started);
        WorkItemsApi.SetDue(123, due);
        WorkItem item = WorkItemsApi.Get(123)!;
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();

        Assert.Equal(Ui.Period(started, due), board.FindAll("article.card").First(c => c.GetAttribute("data-id") == "123").QuerySelector(".tag.dates")!.TextContent.Trim());
        ShowList();
        board.Eventually(() => Assert.NotEmpty(board.FindAll("tr[data-id='123']")));
        string row = board.Find("tr[data-id='123']").TextContent;
        Assert.Contains(Ui.Day(started), row);
        Assert.Contains(Ui.Day(due), row);
        Assert.Contains(Ui.Day(DateOnly.FromDateTime(item.Modified)), row);

        IRenderedComponent<WorkItemPage> page = ctx.Render<WorkItemPage>(p => p.Add(c => c.Id, 123));
        Assert.Equal(Ui.Day(item.Moved ?? DateOnly.FromDateTime(item.Created)), page.Find(".moved-field .chip").TextContent.Trim());

        IRenderedComponent<ExaminePanel> panel = ctx.Render<ExaminePanel>();
        ctx.Services.GetRequiredService<UiService>().Examine((repo.Root, 123));
        panel.Eventually(() => Assert.Equal(Ui.DayWithYear(started), panel.Find("[data-line='started'] b").TextContent.Trim()));
        Assert.Equal(Ui.DayWithYear(due), panel.Find("[data-line='due'] b").TextContent.Trim());
        Assert.Equal(Ui.DayWithYear(DateOnly.FromDateTime(item.Modified)), panel.Find("[data-line='updated'] b").TextContent.Trim());
    }

    [Fact(DisplayName = "The worklog's day headings are written in the user's culture like every other day")]
    public async Task TheWorklogWritesDaysInTheUsersCulture()
    {
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        await using BunitContext ctx = NewContext(time);
        IRenderedComponent<Worklog> log = ctx.Render<Worklog>(p => p.Add(c => c.Item, WorkItemsApi.Get(123)!).Add(c => c.Root, repo.Root));

        Assert.Equal($"Today · {Ui.Day(new DateOnly(2026, 11, 16))}", log.Find(".wk-day.today .wk-date-t").TextContent.Trim());
    }

    [Fact(DisplayName = "The period picker and the side menu name a period across a year end the same way")]
    public async Task PeriodsAcrossAYearEnd()
    {
        await using BunitContext ctx = NewContext();
        DateOnly from = new(2025, 12, 28), to = new(2026, 1, 3);
        IRenderedComponent<DateRange> range = ctx.Render<DateRange>(p => p.Add(c => c.Start, from).Add(c => c.End, to));

        Assert.Equal("28 Dec 25 – 3 Jan 26", Ui.Period(from, to));
        Assert.Equal(Ui.Period(from, to), range.Find("button.dates-btn").TextContent.Trim());
        Assert.Equal("changed " + Ui.Period(from, to), BoardModel.ChangedLabel(new BoardOptions { ChangedFrom = from, ChangedTo = to }));
        // with times the same days are named the same, the year too
        Assert.Equal("changed 28 Dec 25 20:00 – 3 Jan 26 06:00", BoardModel.ChangedLabel(new BoardOptions { ChangedFrom = from, ChangedTo = to, ChangedFromTime = new(20, 0), ChangedToTime = new(6, 0) }));
    }

    [Fact(DisplayName = "The period picker's week starts on the weekday names the culture gives")]
    public async Task TheWeekdayHeadsComeFromTheCulture()
    {
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        await using BunitContext ctx = NewContext();
        IRenderedComponent<DateRange> range = ctx.Render<DateRange>();
        range.ClickWhenReady("button.dates-btn");

        range.Eventually(() => Assert.Equal(Ui.WeekdayHeads, range.FindAll(".rdow").Select(d => d.TextContent.Trim())));
        range.Eventually(() => Assert.Equal("Mo", Ui.WeekdayHeads[0]));   // Monday first, as the weeks are
        range.Eventually(() => Assert.Equal(7, Ui.WeekdayHeads.Count));
    }

    [Fact(DisplayName = "Pages count 'today' from the app's clock: the roadmap, the period picker, the worklog, the due columns and the analytics weeks")]
    public async Task PagesCountTodayFromTheAppsClock()
    {
        FakeTimeProvider time = new(new DateTimeOffset(2031, 3, 5, 12, 0, 0, TimeSpan.Zero));   // a Wednesday, years from the real today
        await using BunitContext ctx = NewContext(time);

        IRenderedComponent<Roadmap> roadmap = ctx.Render<Roadmap>(p => p.Add(c => c.Projects, []));
        Assert.Contains("Mar 2031", roadmap.Find(".rm-range").TextContent);

        IRenderedComponent<DateRange> range = ctx.Render<DateRange>();
        range.ClickWhenReady("button.dates-btn");
        range.Eventually(() => Assert.Equal(Ui.MonthLong(new DateOnly(2031, 3, 1)), range.Find(".range-h b").TextContent.Trim()));
        range.Eventually(() => Assert.Contains("today", range.Find(".rd[data-day='2031-03-05']").ClassList));

        IRenderedComponent<Worklog> log = ctx.Render<Worklog>(p => p.Add(c => c.Item, WorkItemsApi.Get(123)!).Add(c => c.Root, repo.Root));
        log.Eventually(() => Assert.Equal("2031-03-05", log.Find(".wk-day.today").GetAttribute("data-day")));

        IRenderedComponent<Analytics> analytics = ctx.Render<Analytics>(p => p
            .Add(c => c.Items, WorkItemsApi.GetAll()).Add(c => c.Features, []).Add(c => c.Events, _ => []).Add(c => c.Charts, AnalyticsModel.Charts(new BoardOptions())));
        range.Eventually(() => Assert.Contains(Ui.Day(new DateOnly(2031, 3, 3)), analytics.Markup));   // the last week of the over-time chart starts on that Monday

        WorkItemsApi.SetDue(123, new DateOnly(2031, 3, 5));
        SettingsStore settings = ctx.Services.GetRequiredService<SettingsStore>();
        settings.SaveBoard(repo.Root, settings.Board(repo.Root) with { GroupBy = "due" });
        IRenderedComponent<BoardPage> board = ctx.Render<BoardPage>();
        board.Eventually(() =>
        {
            AngleSharp.Dom.IElement col = board.FindAll(".col").Single(c => c.QuerySelector(".col-h, header")?.TextContent.Contains("Today") == true);
            Assert.Contains("Health-check endpoint", col.TextContent);
        });
    }

    [Fact(DisplayName = "A status has the same colour on the board, in the list and in analytics, and a priority the same colour and name")]
    public async Task ColoursAreOneTable()
    {
        await using BunitContext ctx = NewContext();
        foreach (string status in new[] { "Planned", "Confirmed", "In Progress", "Blocked", "In Review", "Completed", "completed", "Something else" })
        {
            WorkItem item = WorkItemsApi.Get(123)! with { Status = status };
            Assert.Equal(Ui.Band(status), BoardModel.ColourOf(item, "status"));
            Assert.Equal(Ui.Band(status), Ui.Colour("status", status));
            Assert.Equal($"--fill:var({Ui.Band(status)})", Pill.FillOf("status", status));
        }
        Assert.Equal(["--sky", "--lilac", "--sun", "--peach", "--mint", "--sage", "--rose", "--teal", "--sand", "--green", "--indigo", "--orchid"], Ui.Palette);
        Assert.Equal(Ui.Palette, BoardModel.Palette);

        // the analytics chart wears the board's colour for each value of each field
        IReadOnlyList<WorkItem> items = WorkItemsApi.GetAll();
        foreach (string field in new[] { "status", "label", "priority" })
        {
            ChartData data = AnalyticsModel.Build(new("c", "t", "stacked", "status", field, "items"), items, _ => [], [], new DateTime(2031, 3, 5));
            Assert.NotEmpty(data.Series);
            foreach (Series s in data.Series)
            {
                string key = field == "priority" ? (s.Name == Ui.PriorityLabel("high") ? "high" : "low") : s.Name;
                if (field == "label" && s.Name == "none") continue;
                Assert.Equal(Ui.Colour(field, key), s.ColourKey);
            }
        }
        Assert.Equal("High priority", Ui.PriorityLabel("high"));
        Assert.Equal("Lower priority", Ui.PriorityLabel("low"));
        Assert.Equal(Ui.PriorityLabel("low"), BoardModel.KeyLabel("priority:low", []));
        Assert.Equal(Ui.PriorityLabel("high"), BoardModel.KeyLabel("priority:high", []));
    }

    // A day format written anywhere but Ui.cs is a second table of formats. Data keys (yyyy-MM-dd in an attribute or a file name) are not display.
    [Fact(DisplayName = "No date format string and no 'today' of the system is left in the components or the board's view models, except in Ui.cs")]
    public void NoInlineFormatsLeft()
    {
        string root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "TaskManager", "TaskManager.csproj"))) root = Path.GetDirectoryName(root)!;
        Assert.NotNull(root);
        string[] files = [.. Directory.EnumerateFiles(Path.Combine(root, "TaskManager", "Components"), "*.*", SearchOption.AllDirectories)   // the board's view models live in Components/Pages/<Feature> since the layout move
            .Where(f => (f.EndsWith(".razor") || f.EndsWith(".cs")) && Path.GetFileName(f) != "Ui.cs" && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))];
        Assert.NotEmpty(files);

        Regex format = new("""(ToString\("(?!0\.|N"|D)[^"]*[dMyHhm]{2,}[^"]*"|\{0:[^}]*(d|M|y|H)[^}]*\}|:(d MMM|HH|d MMMM|MMM|ddd))""");
        Regex dataKey = new("""\.ToString\("yyyy-MM-dd"\)""");
        Regex system = new(@"DateTime\.(Today|Now|UtcNow)|DateTimeOffset\.(Now|UtcNow)");
        List<string> found = [];
        foreach (string file in files)
        {
            string[] lines = File.ReadAllLines(file);
            for (int n = 0; n < lines.Length; n++)
            {
                string line = dataKey.Replace(lines[n], "");
                if (format.IsMatch(line) || system.IsMatch(line)) found.Add($"{Path.GetRelativePath(root, file)}:{n + 1}: {lines[n].Trim()}");
            }
        }
        Assert.True(found.Count == 0, "written outside Ui.cs:\n" + string.Join("\n", found));
    }
}
