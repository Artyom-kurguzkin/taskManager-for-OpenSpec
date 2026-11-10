using Features;
using TaskManager.Components;
using Features.PublicDTOs;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.Board;

/// <summary>What the board/list is showing. Remembered per repository in the settings file.</summary>
public sealed record BoardOptions(
    string GroupBy = "status",     // the columns: status | feature | label | priority | contributor | due | started
    string Swimlane = "none",      // the rows (board rows, list outer groups, roadmap groups): none or any column field
    string ColorBy = "status",     // what the card bands, list stripes and roadmap bars are coloured by: none or any column field
    string SortBy = "priority",    // priority | feature | who | modified | created | status | title | id | manual (my order)
    int SortDir = 1,
    string Who = "all",
    string Period = "all",         // due in: all | overdue | today | week | month | none
    string Category = "all",       // legacy; kept for deserialization, unused
    string Unit = "tasks",         // tasks | subtasks
    string View = "board",         // board | list
    bool Compact = false,          // the board: cards show only their title band
    bool ShowSubtasks = false,     // the list: every row shows its subtasks (otherwise only rows opened by their chevron)
    bool ShowHidden = false,       // include work items "deleted" (hidden) in the app
    IReadOnlyList<string>? Hidden = null,   // ColumnKey of columns taken off the board
    IReadOnlyList<string>? Order = null,    // column and card (item:<id>) keys in the user's order; anything not listed keeps its usual place after them
    string? ListSettings = null,            // the list view's grid settings (column widths, order, sort, filters) as JSON
    DateOnly? ChangedFrom = null,           // the filter: only work that changed from this day ...
    DateOnly? ChangedTo = null,             // ... to this one (both set, or both null = off)
    IReadOnlyList<ChartSpec>? Charts = null)   // the Analytics view's charts; null = the two defaults (AnalyticsModel.DefaultCharts)
{
    /// <summary>A changed period is set: items that did not change in it are left out.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ChangedOn => ChangedFrom is not null && ChangedTo is not null;
}

public sealed record BoardColumn(string Key, string Label, IReadOnlyList<WorkItem> Items, IReadOnlyList<string> Lanes);

/// <summary><see cref="Rows"/> is set when the options ask for rows: the section split by the row field, each row spanning every column.</summary>
public sealed record BoardSection(string? Category, IReadOnlyList<BoardColumn> Columns, IReadOnlyList<SwimRow>? Rows = null);

/// <summary>One value of a field (a column or a row): its key, its label and which items have it.</summary>
public sealed record FieldValue(string Key, string Label, Func<WorkItem, bool> Match);

/// <summary>One row across a section's columns. <see cref="Key"/> is the value a drop sets ("" for no rows, <see cref="BoardModel.NoRow"/> for "no value").</summary>
public sealed record SwimRow(string Key, string Label, IReadOnlyList<BoardColumn> Columns)
{
    /// <summary>The row's items, each once (an item in several columns is one item).</summary>
    public IReadOnlyList<WorkItem> Items => [.. Columns.SelectMany(c => c.Items).DistinctBy(i => i.Id)];
}

/// <summary>
/// One row of the list view: a work item in one of the board's columns (an item in several features appears once per column).
/// An empty column gets one row with no item, so the column still shows as a group. The flat properties feed the grid's
/// sorting and filtering.
/// </summary>
public sealed record GridRow(int Group, BoardColumn Column, string? Category, WorkItem? Item, int Position, string Feature, string Root = "", string Project = "", string Swim = "", string SwimLabel = "")
{
    public string Key => $"{Group}:{Swim}:{Item?.Id ?? 0}";   // an item in two row values of one column (features, people) is two rows
    public int Id => Item?.Id ?? 0;
    public string Title => Item?.Title ?? "";
    public string Status => Item?.Status ?? "";
    public string Label => Item?.Label ?? "";
    public string Contributors => Item is null ? "" : string.Join(", ", Item.Contributors);
    public string Priority => Item?.Priority ?? "";
    public int Progress => Item is { Subtasks.Count: > 0 } i ? i.Subtasks.Count(s => s.Done) * 100 / i.Subtasks.Count : 0;
    public DateTime? Modified => Item?.Modified;
    public DateTime? Started => Item?.Started?.ToDateTime(TimeOnly.MinValue);
    public DateTime? Due => Item?.Due?.ToDateTime(TimeOnly.MinValue);
    public DateTime? Moved => Item is null ? null : Item.Moved?.ToDateTime(TimeOnly.MinValue) ?? Item.Created.Date;   // never recorded: the creation date
}

/// <summary>Pure arrangement of work items into board sections and columns (no IO, so it is unit-tested directly).</summary>
public static class BoardModel
{
    /// <summary>The fields columns, rows and colour can use, in the side menu's order.</summary>
    public static readonly IReadOnlyList<string> Fields = ["status", "feature", "label", "priority", "contributor", "due", "started"];

    /// <summary>Row key of the row holding the items without a value (no feature, no label, no contributor): "" means "no rows at all".</summary>
    public const string NoRow = "~";

    public static IReadOnlyList<BoardSection> Build(
        IReadOnlyList<WorkItem> items, IReadOnlyList<Feature> features, IReadOnlyDictionary<string, IReadOnlyList<string>> lanes, BoardOptions o, DateOnly? today = null)
    {
        DateOnly now = today ?? DateOnly.FromDateTime(DateTime.Today);
        List<WorkItem> visible = Visible(items, o, now);
        BoardColumn Col(FieldValue v) =>
            new(v.Key, v.Label, visible.Where(v.Match).ToList(), lanes.TryGetValue(v.Key, out IReadOnlyList<string>? l) ? l : []);

        // columns: the items without a value first (never hidden or reordered), then the values in the user's order
        IReadOnlyList<FieldValue> values = Values(o.GroupBy, items, visible, features, now);
        List<BoardColumn> columns =
        [
            .. values.Where(v => v.Key == Unassigned).Select(Col),
            .. Arrange(values.Where(v => v.Key != Unassigned), v => ColumnKey(o, v.Key), o).Select(Col),
        ];

        // rows: the same values for the row field, in the user's order without the hidden ones, each a copy of every column holding only the row's items
        List<SwimRow>? rows = o.Swimlane == NoSwimlane ? null :
        [
            .. Arrange(Values(o.Swimlane, items, visible, features, now), v => RowKey(o.Swimlane, v.Key == Unassigned ? NoRow : v.Key), o).Select(v =>
                new SwimRow(v.Key == Unassigned ? NoRow : v.Key, v.Label, [.. columns.Select(c => c with { Items = [.. c.Items.Where(v.Match)] })])),
        ];
        return [new(null, columns, rows)];
    }

    /// <summary>The items the options let through (contributor, due period and changed period), in the sort order.</summary>
    private static List<WorkItem> Visible(IReadOnlyList<WorkItem> items, BoardOptions o, DateOnly now)
    {
        IReadOnlySet<int>? changed = Changed(items, o);   // null: no changed period, nothing is left out for it
        return Sort(items.Where(i => (o.Who == "all" || i.Contributors.Contains(o.Who)) && InPeriod(i.Due, o.Period, now) && (changed is null || changed.Contains(i.Id))), o).ToList();
    }

    /// <summary>The items the options' contributor, due period and changed period let through (the Analytics view charts these).</summary>
    public static IReadOnlyList<WorkItem> Filtered(IReadOnlyList<WorkItem> items, BoardOptions o, DateOnly? today = null) => Visible(items, o, today ?? DateOnly.FromDateTime(DateTime.Today));

    /// <summary>
    /// The values of a field, as columns and rows both show them: the items without a value first (only when some visible item has none),
    /// then the field's values. A multi-valued field (feature, contributor) matches an item against each of its values.
    /// </summary>
    public static IReadOnlyList<FieldValue> Values(string field, IReadOnlyList<WorkItem> all, IReadOnlyList<WorkItem> visible, IReadOnlyList<Feature> features, DateOnly now)
    {
        List<FieldValue> values = [];
        void Loose(bool any, string label, Func<WorkItem, bool> none) { if (any) values.Add(new(Unassigned, label, none)); }
        switch (field)
        {
            case "contributor":
                Loose(visible.Any(i => i.Contributors.Count == 0), "No contributors", i => i.Contributors.Count == 0);
                values.AddRange(all.SelectMany(i => i.Contributors).Distinct().Order(StringComparer.OrdinalIgnoreCase).Select(p => new FieldValue(p, p, i => i.Contributors.Contains(p))));
                break;
            case "feature":
                Loose(visible.Any(i => i.Features.Count == 0), "Unassigned", i => i.Features.Count == 0);
                values.AddRange(features.Select(f => new FieldValue(f.Path, f.Name, i => i.Features.Any(x => x.Path == f.Path))));
                break;
            case "label":
                Loose(visible.Any(i => string.IsNullOrEmpty(i.Label)), "Unlabeled", i => string.IsNullOrEmpty(i.Label));
                IEnumerable<string> typed = all.Select(i => i.Label).Where(l => !string.IsNullOrEmpty(l)).Select(l => l!).Except(WorkItemsApi.Labels).Distinct().Order();   // a label typed into a change by hand
                values.AddRange(WorkItemsApi.Labels.Concat(typed).Select(l => new FieldValue(l, l, i => i.Label == l)));
                break;
            case "priority":
                values.Add(new("high", "High priority", i => i.Priority == "high"));
                values.Add(new("low", "Lower priority", i => i.Priority != "high"));
                break;
            case "due":
                values.AddRange(DuePeriods.Select(p => new FieldValue(p.Key, p.Label, i => Period(i.Due, now) == p.Key)));
                break;
            case "started":
                values.AddRange(StartPeriods.Select(p => new FieldValue(p.Key, p.Label, i => StartPeriod(i.Started, now) == p.Key)));
                break;
            default:
                values.AddRange(WorkItemsApi.Statuses.Select(s => new FieldValue(s, s, i => i.Status == s)));
                break;
        }
        return values;
    }

    /// <summary>A section's rows: one row across all its columns when the options ask for none.</summary>
    public static IReadOnlyList<SwimRow> Swimlanes(BoardSection section) => section.Rows ?? [new("", "", section.Columns)];

    /// <summary>
    /// The list view's rows, in board order: column by column, and with rows set the row values inside each column (in the board's row
    /// order, leaving out the ones with no items there), each cell's items in their sorted order. A group is a column
    /// (<see cref="GridRow.Group"/>) and carries the whole column; an empty column keeps one row with no item, so it still shows.
    /// </summary>
    public static IReadOnlyList<GridRow> Rows(IReadOnlyList<BoardSection> sections, IReadOnlyDictionary<string, string> featureNames,
        string root = "", string project = "", int firstGroup = 0)
    {
        List<GridRow> rows = [];
        int group = firstGroup, position = 0;
        string FeatureOf(WorkItem it) => it.Features.FirstOrDefault() is { } f ? featureNames.GetValueOrDefault(f.Path, f.Path) : "";
        foreach (BoardSection s in sections)
        for (int ci = 0; ci < s.Columns.Count; ci++)
        {
            BoardColumn c = s.Columns[ci];
            int first = rows.Count;
            foreach (SwimRow swim in Swimlanes(s))
                foreach (WorkItem it in swim.Columns[ci].Items)
                    rows.Add(new(group, c, s.Category, it, position++, FeatureOf(it), root, project, swim.Key, swim.Label));
            if (rows.Count == first) rows.Add(new(group, c, s.Category, null, position++, "", root, project));
            group++;
        }
        return rows;
    }

    /// <summary>The list column that repeats a field (every row of a group has the same value there), or null for none.</summary>
    public static string? FieldColumn(string field) => field switch
    {
        "status" => "Status",
        "label" => "Label",
        "feature" => "Feature",
        "priority" => "Priority",
        "contributor" => "Contributors",
        "due" => "Due",
        "started" => "Started",
        _ => null,
    };

    /// <summary>The list column that repeats the columns' grouping, or null.</summary>
    public static string? GroupedColumn(BoardOptions o) => FieldColumn(o.GroupBy);

    // ── Colour: a CSS variable for each item, from any field ──
    /// <summary>The twelve categorical colour tokens (CSS variables in app.css) features and contributors take in turn; --stone is the neutral "none".</summary>
    public static readonly IReadOnlyList<string> Palette = ["--sky", "--lilac", "--sun", "--peach", "--mint", "--sage", "--rose", "--teal", "--sand", "--green", "--indigo", "--orchid"];

    /// <summary>
    /// The colour token (a CSS variable name from the palette) an item shows for a field: status → its band colour, label → the label pill's,
    /// priority → high peach / lower neutral, due and started → by period, feature and contributor → the palette entry at the first value's place in <paramref name="map"/> (see <see cref="ColourMap"/>; a value
    /// missing from it gets a stable pick by name), none (or anything else) → neutral.
    /// </summary>
    public static string ColourOf(WorkItem item, string field, DateOnly? today = null, IReadOnlyDictionary<string, string>? map = null)
    {
        DateOnly now = today ?? DateOnly.FromDateTime(DateTime.Today);
        return field switch
        {
            "status" => Ui.Band(item.Status),
            "label" => Ui.LabelColor(item.Label ?? ""),
            "priority" => Ui.PriorityColor(item.Priority),
            "due" => Period(item.Due, now) switch { "overdue" => "--peach", "today" => "--sun", "week" => "--sky", "next" => "--mint", "later" => "--sage", _ => "--stone" },
            "started" => StartPeriod(item.Started, now) switch { "earlier" => "--sage", "week" => "--sun", "next" => "--sky", "later" => "--lilac", _ => "--stone" },
            "feature" => item.Features.Count > 0 ? Pick(item.Features[0].Path, map) : "--stone",
            "contributor" => item.Contributors.Count > 0 ? Pick(item.Contributors[0], map) : "--stone",
            _ => "--stone",
        };
    }

    /// <summary>
    /// The colour of each value of a feature or contributor colour field: the palette entry at the value's place in the field's value list
    /// (the board's feature list, the people list), cycling after twelve. Built from every item, so a filter never repaints a value.
    /// Other fields have none.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ColourMap(string field, IReadOnlyList<WorkItem> all, IReadOnlyList<Feature> features)
    {
        if (field is not ("feature" or "contributor")) return new Dictionary<string, string>();
        string[] keys = [.. Values(field, all, all, features, DateOnly.FromDateTime(DateTime.Today)).Where(v => v.Key != Unassigned).Select(v => v.Key)];
        return keys.Select((k, n) => (k, n)).ToDictionary(x => x.k, x => Palette[x.n % Palette.Count]);
    }

    /// <summary>The value's colour from the map, else a stable pick from the palette by its name (string.GetHashCode changes per process).</summary>
    private static string Pick(string name, IReadOnlyDictionary<string, string>? map)
    {
        if (map is not null && map.TryGetValue(name, out string? token)) return token;
        uint h = 2166136261;
        foreach (char c in name) h = (h ^ c) * 16777619;
        return Palette[(int)(h % (uint)Palette.Count)];
    }

    /// <summary>What the colours mean: each value of the colour field that some shown item has, with its colour (the side menu's legend).</summary>
    public static IReadOnlyList<(string Label, string Colour)> Legend(IReadOnlyList<WorkItem> items, IReadOnlyList<Feature> features, BoardOptions o, DateOnly? today = null)
    {
        if (!Fields.Contains(o.ColorBy)) return [];
        DateOnly now = today ?? DateOnly.FromDateTime(DateTime.Today);
        List<WorkItem> visible = Visible(items, o, now);
        // an item's colour comes from its first feature or contributor, so only those count for the legend
        string? Primary(WorkItem i) => o.ColorBy switch { "feature" => i.Features.FirstOrDefault()?.Path, "contributor" => i.Contributors.FirstOrDefault(), _ => null };
        bool Has(FieldValue v, WorkItem i) => o.ColorBy is "feature" or "contributor" ? (Primary(i) ?? Unassigned) == v.Key : v.Match(i);
        IReadOnlyDictionary<string, string> map = ColourMap(o.ColorBy, items, features);
        List<(string Label, string Colour)> legend = [];
        foreach (FieldValue v in Values(o.ColorBy, items, visible, features, now))
            if (visible.FirstOrDefault(i => Has(v, i)) is { } first) legend.Add((v.Label, ColourOf(first, o.ColorBy, now, map)));
        return legend;
    }

    // ── The changed period: the side menu's filter to what changed between two days ──
    /// <summary>
    /// The ids of the items that changed from <paramref name="from"/> to <paramref name="to"/>, both days included, by local day: some of the
    /// times <paramref name="logged"/> gives for it (its events and notes), its creation, or its files' last write falls in it.
    /// </summary>
    public static IReadOnlySet<int> ChangedIn(IReadOnlyList<WorkItem> items, Func<WorkItem, IEnumerable<DateTime>> logged, DateOnly from, DateOnly to)
    {
        bool In(DateTime t) => DateOnly.FromDateTime(t) is var d && d >= from && d <= to;
        return items.Where(i => In(i.Created) || In(i.Modified) || logged(i).Any(In)).Select(i => i.Id).ToHashSet();
    }

    /// <summary>
    /// What <see cref="ChangedIn"/> gives for the board's changed period, reading each item's events.log and notes of the current repository
    /// (call it inside the project's <c>Workspace.In</c>); null while no period is set, which is "nothing is left out".
    /// </summary>
    public static IReadOnlySet<int>? Changed(IReadOnlyList<WorkItem> items, BoardOptions o)
    {
        if (o.ChangedFrom is not DateOnly from || o.ChangedTo is not DateOnly to) return null;
        ILookup<int, DateTime> notes = Notes.NotesApi.GetAll().ToLookup(n => n.WorkItemId, n => n.Created);
        return ChangedIn(items, i => WorkItemsApi.GetEvents(i.Id).Select(e => e.At).Concat(notes[i.Id]), from, to);
    }

    /// <summary>How many items the board shows with the changed period, and how many the other filters leave without it: the legend's "3 of 36 shown". Null while no period is set.</summary>
    public static (int InPeriod, int Others)? ShownCount(IReadOnlyList<WorkItem> items, BoardOptions o, DateOnly? today = null)
    {
        if (!o.ChangedOn) return null;
        DateOnly now = today ?? DateOnly.FromDateTime(DateTime.Today);
        return (Visible(items, o, now).Count, Visible(items, o with { ChangedFrom = null, ChangedTo = null }, now).Count);
    }

    // ── Time periods (due and start dates) ──
    public static readonly IReadOnlyList<(string Key, string Label)> DuePeriods =
        [("overdue", "Overdue"), ("today", "Today"), ("week", "This week"), ("next", "Next week"), ("later", "Later"), ("none", "No due date")];
    public static readonly IReadOnlyList<(string Key, string Label)> StartPeriods =
        [("earlier", "Earlier"), ("week", "This week"), ("next", "Next week"), ("later", "Later"), ("none", "Not started")];

    /// <summary>Which due period a day falls in, seen from today: overdue | today | week (to Sunday) | next (next week) | later | none.</summary>
    public static string Period(DateOnly? day, DateOnly today)
    {
        if (day is not DateOnly d) return "none";
        DateOnly sunday = RoadmapModel.Monday(today).AddDays(6);
        return d < today ? "overdue" : d == today ? "today" : d <= sunday ? "week" : d <= sunday.AddDays(7) ? "next" : "later";
    }

    /// <summary>Which period work started in: earlier (before this week) | week | next | later | none.</summary>
    public static string StartPeriod(DateOnly? day, DateOnly today)
    {
        if (day is not DateOnly d) return "none";
        DateOnly monday = RoadmapModel.Monday(today);
        return d < monday ? "earlier" : d < monday.AddDays(7) ? "week" : d < monday.AddDays(14) ? "next" : "later";
    }

    /// <summary>The side menu's period filter, on the due date: all | overdue | today | week | month | none.</summary>
    public static bool InPeriod(DateOnly? due, string period, DateOnly today) => period switch
    {
        "overdue" => due < today,
        "today" => due == today,
        "week" => due >= today && due <= RoadmapModel.Monday(today).AddDays(6),
        "month" => due >= today && due <= new DateOnly(today.Year, today.Month, 1).AddMonths(1).AddDays(-1),
        "none" => due is null,
        _ => true,
    };

    /// <summary>Key of a list column (Title, Status, …) in the hidden list, next to the board columns' keys.</summary>
    public static string ListColumnKey(string title) => "col:" + title;

    /// <summary>Column key for work items without a feature (grouping by feature) or without contributors (by contributor).</summary>
    public const string Unassigned = "";

    public static string FeatureKey(string path) => "feature:" + path;

    /// <summary>Key of a column under the current grouping, for the saved order / hidden lists.</summary>
    public static string ColumnKey(BoardOptions o, string column) => o.GroupBy switch
    {
        "feature" => FeatureKey(column),
        "label" => "label:" + column,
        "contributor" => "who:" + column,
        "priority" => "priority:" + column,
        "due" => "due:" + column,
        "started" => "started:" + column,
        _ => "status:" + column,
    };

    /// <summary>Key of a row (<see cref="SwimRow.Key"/>) under a row field, for the saved order / hidden lists: each field keeps its own.</summary>
    public static string RowKey(string field, string row) => $"row:{field}:{row}";

    /// <summary>The rows the user hid under the current row field, in their usual order, with how many items (or subtasks, per the unit) each would hold.</summary>
    public static IReadOnlyList<(string Key, string Label, int Count)> HiddenRows(
        IReadOnlyList<WorkItem> items, IReadOnlyList<Feature> features, IReadOnlyDictionary<string, IReadOnlyList<string>> lanes, BoardOptions o)
    {
        if (o.Swimlane == NoSwimlane) return [];
        BoardOptions all = o with { Hidden = o.Hidden?.Where(k => !k.StartsWith($"row:{o.Swimlane}:")).ToList() };   // every row, but still without the hidden columns
        return [.. Build(items, features, lanes, all).SelectMany(Swimlanes)
            .Select(r => (Key: RowKey(o.Swimlane, r.Key), r.Label, Count: Count(r.Items, o.Unit)))
            .Where(r => IsHidden(o, r.Key))];
    }

    /// <summary>Readable name of a saved key (for the side menu's list of hidden columns).</summary>
    public static string KeyLabel(string key, IReadOnlyList<Feature> features) => key.Split(':', 2) switch
    {
        ["feature", string path] => features.FirstOrDefault(f => f.Path == path)?.Name ?? path,
        ["due", string p] => DuePeriods.FirstOrDefault(x => x.Key == p).Label ?? p,
        ["started", string p] => StartPeriods.FirstOrDefault(x => x.Key == p).Label ?? p,
        ["priority", string p] => p == "high" ? "High priority" : "Lower priority",
        [_, string name] => name,
        _ => key,
    };

    /// <summary>How many items (or subtasks, per the unit) each hidden column would hold, under the current filters.</summary>
    public static Dictionary<string, int> HiddenCounts(
        IReadOnlyList<WorkItem> items, IReadOnlyList<Feature> features, IReadOnlyDictionary<string, IReadOnlyList<string>> lanes, BoardOptions o)
    {
        Dictionary<string, int> counts = [];
        foreach (BoardColumn col in Build(items, features, lanes, o with { Hidden = null }).SelectMany(s => s.Columns))
            if (IsHidden(o, ColumnKey(o, col.Key))) counts[ColumnKey(o, col.Key)] = Count(col.Items, o.Unit);
        return counts;
    }

    public static bool IsHidden(BoardOptions o, string key) => o.Hidden?.Contains(key) ?? false;

    public static BoardOptions ToggleHidden(BoardOptions o, string key) =>
        o with { Hidden = IsHidden(o, key) ? o.Hidden!.Where(k => k != key).ToList() : [.. o.Hidden ?? [], key] };

    /// <summary>The user's order first, then the rest in their usual order; hidden ones dropped unless asked for.</summary>
    public static IEnumerable<T> Arrange<T>(IEnumerable<T> items, Func<T, string> key, BoardOptions o, bool includeHidden = false)
    {
        List<string> order = o.Order?.ToList() ?? [];
        return items.Where(x => includeHidden || !IsHidden(o, key(x)))
            .Select((x, i) => (x, i))
            .OrderBy(t => order.IndexOf(key(t.x)) is int p and >= 0 ? p : order.Count + t.i)
            .Select(t => t.x);
    }

    /// <summary>
    /// Drag-and-drop: <paramref name="moving"/> keys land together, in their given order, in front of <paramref name="before"/>
    /// among <paramref name="siblings"/> (given in their shown order), or at the end when it is null.
    /// Returns <paramref name="o"/> itself when nothing would change.
    /// </summary>
    public static BoardOptions Insert(BoardOptions o, IReadOnlyList<string> siblings, IReadOnlyList<string> moving, string? before)
    {
        List<string> list = siblings.Except(moving).ToList();
        int at = before is null ? list.Count : list.IndexOf(before);
        if (at < 0) return o;   // dropped in front of itself, or of a column it doesn't share a row with
        list.InsertRange(at, moving);
        return list.SequenceEqual(siblings) ? o : o with { Order = [.. (o.Order ?? []).Except(list), .. list] };
    }

    /// <summary>Key of a card in the saved order (same list as column keys; the prefixes never collide).</summary>
    public static string ItemKey(int id) => "item:" + id;

    /// <summary>Cards dropped into <paramref name="column"/> (ids in their shown order) at the gap; any change switches the sort to "my order".</summary>
    public static BoardOptions PlaceCards(BoardOptions o, IReadOnlyList<int> column, IReadOnlyList<int> moving, int? before) =>
        Insert(o, [.. column.Select(ItemKey)], [.. moving.Select(ItemKey)], before is int b ? ItemKey(b) : null) is var placed && !ReferenceEquals(placed, o)
            ? placed with { SortBy = "manual", SortDir = 1 }
            : o;

    /// <summary>
    /// Arrow key: the selected cards (<paramref name="moving"/>, a subset of <paramref name="column"/>) move one step up (-1) or down (+1).
    /// A scattered selection first gathers into a block, at its top card going up or its bottom card going down.
    /// </summary>
    public static BoardOptions Step(BoardOptions o, IReadOnlyList<int> column, IReadOnlyList<int> moving, int dir)
    {
        List<int> block = column.Where(moving.Contains).ToList();
        if (block.Count == 0) return o;
        string? before = StepBefore([.. column.Select(ItemKey)], [.. block.Select(ItemKey)], dir);
        return PlaceCards(o, column, block, before is null ? null : int.Parse(before["item:".Length..]));
    }

    /// <summary>
    /// Arrow key on selected row headers (<paramref name="moving"/>, a subset of <paramref name="siblings"/>, the rows in their shown order):
    /// the same step as <see cref="Step"/>, on keys. Returns <paramref name="o"/> itself when nothing would change.
    /// </summary>
    public static BoardOptions StepKeys(BoardOptions o, IReadOnlyList<string> siblings, IReadOnlyList<string> moving, int dir)
    {
        List<string> block = siblings.Where(moving.Contains).ToList();
        return block.Count == 0 ? o : Insert(o, siblings, block, StepBefore(siblings, block, dir));
    }

    /// <summary>The key a block (in <paramref name="all"/>'s order) lands in front of after one step up (-1) or down (+1), null for the end.</summary>
    private static string? StepBefore(IReadOnlyList<string> all, IReadOnlyList<string> block, int dir)
    {
        int top = all.ToList().IndexOf(block[0]), bottom = all.ToList().IndexOf(block[^1]);
        bool together = bottom - top + 1 == block.Count;
        List<string> others = all.Except(block).ToList();
        int at = !together ? (dir < 0 ? top : bottom - block.Count + 1)   // gather: the block starts where its top (or ends where its bottom) item is
               : Math.Clamp(top + dir, 0, others.Count);
        return at < others.Count ? others[at] : null;
    }

    /// <summary>The row field meaning "no rows".</summary>
    public const string NoSwimlane = "none";

    /// <summary>The list column that repeats the rows (every row of a group has the same value there), or null.</summary>
    public static string? SwimColumn(BoardOptions o) => FieldColumn(o.Swimlane);

    public static int Count(IEnumerable<WorkItem> items, string unit) => unit == "subtasks" ? items.Sum(i => i.Subtasks.Count) : items.Count();

    public static IEnumerable<WorkItem> Sort(IEnumerable<WorkItem> items, BoardOptions o)
    {
        Comparison<WorkItem> byPriority = (a, b) => (a.Priority == b.Priority ? 0 : a.Priority == "high" ? -1 : 1) is int p && p != 0 ? p : b.Modified.CompareTo(a.Modified);
        List<string> placed = o.Order?.ToList() ?? [];   // ponytail: IndexOf per compare, a dictionary if boards reach thousands of cards
        int Rank(WorkItem w) => placed.IndexOf(ItemKey(w.Id)) is int r and >= 0 ? r : int.MaxValue;
        Comparison<WorkItem> cmp = o.SortBy switch
        {
            "feature" => (a, b) => string.CompareOrdinal(a.Features.FirstOrDefault()?.Path ?? "~", b.Features.FirstOrDefault()?.Path ?? "~"),
            "label" => (a, b) => string.Compare(a.Label ?? "~", b.Label ?? "~", StringComparison.OrdinalIgnoreCase),
            "who" => (a, b) => string.Compare(a.Contributors.FirstOrDefault() ?? "~", b.Contributors.FirstOrDefault() ?? "~", StringComparison.OrdinalIgnoreCase),
            "modified" => (a, b) => b.Modified.CompareTo(a.Modified),
            "created" => (a, b) => b.Created.CompareTo(a.Created),
            "status" => (a, b) => IndexOf(a.Status).CompareTo(IndexOf(b.Status)),
            "title" => (a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase),
            "id" => (a, b) => a.Id.CompareTo(b.Id),
            "manual" => (a, b) => Rank(a).CompareTo(Rank(b)) is int r && r != 0 ? r : byPriority(a, b),
            _ => byPriority,
        };
        List<WorkItem> list = items.ToList();
        list.Sort((a, b) => (cmp(a, b) is int c && c != 0 ? c : a.Id.CompareTo(b.Id)) * o.SortDir);
        return list;
    }

    private static int IndexOf(string status) => WorkItemsApi.Statuses is IReadOnlyList<string> s ? s.ToList().IndexOf(status) : 0;
}
