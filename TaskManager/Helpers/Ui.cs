using Markdig;
using Microsoft.AspNetCore.Components;
using Notes;
using OpenSpecStore;
using WorkItems;
using TaskManager.AppState;

namespace TaskManager.Helpers;

/// <summary>Small shared view helpers: status band colours and the inline line icons from the design system.</summary>
public static class Ui
{
    public static string Band(string status) => status switch
    {
        "Planned" => "--sky",
        "Confirmed" => "--lilac",
        "In Progress" => "--sun",
        "Blocked" => "--peach",
        "In Review" => "--mint",
        "Completed" => "--sage",
        _ => WorkItemsApi.LookAlikeOf(status) is string fixedStatus ? Band(fixedStatus) : "--stone",   // "completed" and "in progress" wear the fixed status's colour
    };
    /// <summary>What a window's event handlers and reads run inside: a separate window (its <see cref="AppMode.Repo"/>) keeps to its own project whatever tab the main window is on; null for the main window.</summary>
    public static IDisposable? WindowScope(IServiceProvider? services) =>
        services?.GetService(typeof(AppMode)) is AppMode { Repo: string repo } && services.GetService(typeof(Workspace)) is Workspace workspace ? workspace.Scope(repo) : null;

    // ---- the clock: every "today" of the host is the app's TimeProvider (a test moves it by hand); the system's is the default ----

    /// <summary>The app's clock: the TimeProvider registered in <paramref name="services"/>, the system's when none is.</summary>
    public static TimeProvider ClockOf(IServiceProvider? services) => services?.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;

    /// <summary>The local day by <paramref name="clock"/> (the system's when null).</summary>
    public static DateOnly Today(TimeProvider? clock = null) => DateOnly.FromDateTime((clock ?? TimeProvider.System).GetLocalNow().DateTime);

    /// <summary>The local date and time by <paramref name="clock"/> (the system's when null).</summary>
    public static DateTime Now(TimeProvider? clock = null) => (clock ?? TimeProvider.System).GetLocalNow().DateTime;

    // ---- dates: the one place the board's dates, times and periods are written, in the user's culture like every other day on the board ----
    private static System.Globalization.CultureInfo Inv => System.Globalization.CultureInfo.CurrentCulture;

    /// <summary>"9 Nov": a day, short.</summary>
    public static string Day(DateOnly d) => d.ToString("d MMM", Inv);

    /// <summary>The day of a moment: "9 Nov".</summary>
    public static string Day(DateTime t) => Day(DateOnly.FromDateTime(t));

    /// <summary>"9 Nov 2026": a day with its year (where it may be another year than this one: the worklog's earlier days, the examine panel).</summary>
    public static string DayWithYear(DateOnly d) => d.ToString("d MMM yyyy", Inv);

    /// <summary>"9 November 2026": a day in full (a tooltip).</summary>
    public static string DayLong(DateOnly d) => d.ToString("d MMMM yyyy", Inv);

    /// <summary>"Mon, Nov 9": a day with its weekday (the roadmap's group bar).</summary>
    public static string DayWithWeekday(DateOnly d) => d.ToString("ddd, MMM d", Inv);

    /// <summary>"15:34": a time of day.</summary>
    public static string Time(TimeOnly t) => t.ToString("HH\\:mm", Inv);

    /// <summary>"15:34": the time of day of a moment.</summary>
    public static string Time(DateTime t) => t.ToString("HH\\:mm", Inv);

    /// <summary>"9 Nov 15:34": a day and a time of day (an agent's mark).</summary>
    public static string DayTime(DateTime t) => Day(t) + " " + Time(t);

    /// <summary>"2026-11-09 15:34": a moment in full, sortable (a note's created and last modified lines).</summary>
    public static string Moment(DateTime t) => t.ToString("yyyy-MM-dd HH\\:mm", Inv);

    /// <summary>"Nov 2026": a month, short.</summary>
    public static string Month(DateOnly d) => d.ToString("MMM yyyy", Inv);

    /// <summary>"November 2026": a month in full.</summary>
    public static string MonthLong(DateOnly d) => d.ToString("MMMM yyyy", Inv);

    /// <summary>"November": the month's name alone (a quarter's header).</summary>
    public static string MonthName(DateOnly d) => d.ToString("MMMM", Inv);

    /// <summary>The two-letter weekday heads of a calendar, Monday first as the board's weeks are ("Mo" … "Su" in English).</summary>
    public static IReadOnlyList<string> WeekdayHeads
    {
        get
        {
            string[] names = Inv.DateTimeFormat.AbbreviatedDayNames;   // Sunday first
            return [.. Enumerable.Range(1, 7).Select(n => names[n % 7] is { Length: > 2 } name ? name[..2] : names[n % 7])];
        }
    }

    /// <summary>"15:34. 16 Nov 26": when something last changed, the time then the day with a two-digit year.</summary>
    public static string Stamp(DateTime t) => Time(t) + ". " + t.ToString("d MMM yy", Inv);

    private static string DayShortYear(DateOnly d) => d.ToString("d MMM yy", Inv);

    /// <summary>
    /// A planned start and end as one period: "9–16 Nov" in one month, "28 Oct – 3 Nov" across months, "9 Nov 25 – 3 Jan 26" across years,
    /// "16 Nov" for one day, "from 9 Nov" with only a start, "due 16 Nov" with only an end; "" with neither. With times of day
    /// (the period picker, the changed period) each end carries its own: "9 Nov 14:00 – 10 Nov 09:30", "9 Nov 14:00 – 16:00" in one day.
    /// </summary>
    public static string Period(DateOnly? start, DateOnly? due, TimeOnly? startTime = null, TimeOnly? dueTime = null)
    {
        if (startTime is null && dueTime is null) return (start, due) switch
        {
            (DateOnly s, DateOnly e) when s == e => Day(s),
            (DateOnly s, DateOnly e) when s.Year != e.Year => $"{DayShortYear(s)} – {DayShortYear(e)}",
            (DateOnly s, DateOnly e) when s.Month != e.Month => $"{Day(s)} – {Day(e)}",
            (DateOnly s, DateOnly e) => $"{s.Day}–{Day(e)}",
            (DateOnly s, null) => "from " + Day(s),
            (null, DateOnly e) => "due " + Day(e),
            _ => "",
        };
        return (start, due) switch
        {
            (DateOnly s, DateOnly e) when s == e => (startTime, dueTime) switch
            {
                (TimeOnly a, TimeOnly b) => $"{Day(s)} {Time(a)} – {Time(b)}",
                (TimeOnly a, null) => $"{Day(s)} from {Time(a)}",
                (null, TimeOnly b) => $"{Day(s)} until {Time(b)}",
                _ => Day(s),
            },
            (DateOnly s, DateOnly e) =>
                $"{(s.Year != e.Year ? DayShortYear(s) : Day(s))}{(startTime is TimeOnly a ? " " + Time(a) : "")} – {(s.Year != e.Year ? DayShortYear(e) : Day(e))}{(dueTime is TimeOnly b ? " " + Time(b) : "")}",
            _ => Period(start, due),
        };
    }

    // ---- colours: the one table of colour tokens (CSS variables in app.css) for statuses, labels, priorities, periods, features and people ----

    /// <summary>The twelve categorical colour tokens features and contributors take in turn; --stone is the neutral "none".</summary>
    public static readonly IReadOnlyList<string> Palette = ["--sky", "--lilac", "--sun", "--peach", "--mint", "--sage", "--rose", "--teal", "--sand", "--green", "--indigo", "--orchid"];

    /// <summary>The value's colour from <paramref name="map"/>, else a stable pick from the palette by its name (string.GetHashCode changes per process).</summary>
    public static string Pick(string name, IReadOnlyDictionary<string, string>? map = null)
    {
        if (map is not null && map.TryGetValue(name, out string? token)) return token;
        uint h = 2166136261;
        foreach (char c in name) h = (h ^ c) * 16777619;
        return Palette[(int)(h % (uint)Palette.Count)];
    }

    /// <summary>
    /// The colour token of a value of a field, wherever it is drawn (card band, list pill, chart series, legend): a status its band, a label the label's, a priority
    /// high peach and lower neutral; a feature or contributor the entry <paramref name="map"/> gives it, neutral without one; anything else neutral.
    /// </summary>
    public static string Colour(string field, string key, IReadOnlyDictionary<string, string>? map = null) => field switch
    {
        "status" => Band(key),
        "label" => LabelColor(key),
        "priority" => PriorityColor(key),
        "feature" or "contributor" => map is not null && map.TryGetValue(key, out string? token) ? token : "--stone",
        _ => "--stone",
    };

    /// <summary>The colour of a due period (overdue, today, week, next, later; none is neutral).</summary>
    public static string DueColor(string period) => period switch { "overdue" => "--peach", "today" => "--sun", "week" => "--sky", "next" => "--mint", "later" => "--sage", _ => "--stone" };

    /// <summary>The colour of a start period (earlier, week, next, later; none is neutral).</summary>
    public static string StartedColor(string period) => period switch { "earlier" => "--sage", "week" => "--sun", "next" => "--sky", "later" => "--lilac", _ => "--stone" };

    public static string PriorityColor(string priority) => priority == "high" ? "--peach" : "--stone";

    /// <summary>What a priority is called where it is a group or a series: "High priority" or "Lower priority".</summary>
    public static string PriorityLabel(string priority) => priority == "high" ? "High priority" : "Lower priority";

    /// <summary>The choices a Pill / Dropdown offers for each work item field, as (value, label).</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> PriorityOptions = [.. WorkItems.WorkItemsApi.Priorities.Select(p => (p, p == "high" ? "high" : "lower"))];   // the values are WorkItems', the one list an agent is held to too
    public static readonly IReadOnlyList<(string Value, string Label)> LabelOptions = [("", "none"), .. WorkItemsApi.Labels.Select(l => (l, l))];

    public static string LabelColor(string label) => label switch
    {
        "bug" => "--peach",
        "feature" => "--sky",
        "enhancement" => "--sage",
        _ => "--stone",
    };

    private static readonly Dictionary<string, string> Paths = new()
    {
        ["cal"] = "<rect x=\"2\" y=\"3\" width=\"12\" height=\"11\" rx=\"2\"/><path d=\"M2 7h12M5 1.5v3M11 1.5v3\"/>",
        ["list"] = "<path d=\"M6 4h8M6 8h8M6 12h8M2.5 4h.5M2.5 8h.5M2.5 12h.5\"/>",
        ["user"] = "<circle cx=\"8\" cy=\"5.5\" r=\"2.8\"/><path d=\"M2.5 14c.8-3 3-4.5 5.5-4.5s4.7 1.5 5.5 4.5\"/>",
        ["chev"] = "<path d=\"M6 4l4 4-4 4\"/>",
        ["caret"] = "<path d=\"M4 6l4 4 4-4\"/>",
        ["sliders"] = "<path d=\"M2 4.5h7M13 4.5h1M2 11.5h2M8 11.5h6\"/><circle cx=\"11\" cy=\"4.5\" r=\"1.8\"/><circle cx=\"6\" cy=\"11.5\" r=\"1.8\"/>",
        // cog: a thick dashed ring draws the teeth
        ["gear"] = "<circle cx=\"8\" cy=\"8\" r=\"5.6\" stroke-width=\"2.6\" stroke-dasharray=\"2.2 2.2\" stroke-linecap=\"butt\"/><circle cx=\"8\" cy=\"8\" r=\"4.2\"/><circle cx=\"8\" cy=\"8\" r=\"1.6\"/>",
        ["menu"] = "<path d=\"M2.5 4h11M2.5 8h11M2.5 12h11\"/>",
        ["board"] = "<rect x=\"2\" y=\"2.5\" width=\"3.4\" height=\"11\" rx=\"1\"/><rect x=\"6.3\" y=\"2.5\" width=\"3.4\" height=\"7\" rx=\"1\"/><rect x=\"10.6\" y=\"2.5\" width=\"3.4\" height=\"9\" rx=\"1\"/>",
        ["plus"] = "<path d=\"M8 3v10M3 8h10\"/>",
        ["compact"] = "<rect x=\"2\" y=\"2.5\" width=\"12\" height=\"2.6\" rx=\"1\"/><rect x=\"2\" y=\"6.7\" width=\"12\" height=\"2.6\" rx=\"1\"/><rect x=\"2\" y=\"10.9\" width=\"12\" height=\"2.6\" rx=\"1\"/>",
        ["back"] = "<path d=\"M14 8H2M6 4L2 8l4 4\"/>",   // <-: previous page
        ["swap"] = "<path d=\"M2 8h12M5 5L2 8l3 3M11 5l3 3-3 3\"/>",   // <->: switch repository
        ["eye"] = "<path d=\"M1.5 8S3.9 3.5 8 3.5 14.5 8 14.5 8 12.1 12.5 8 12.5 1.5 8 1.5 8z\"/><circle cx=\"8\" cy=\"8\" r=\"2\"/>",
        ["eyeoff"] = "<path d=\"M1.5 8S3.9 3.5 8 3.5 14.5 8 14.5 8 12.1 12.5 8 12.5 1.5 8 1.5 8zM2.5 13.5l11-11\"/>",
        ["expand"] = "<path d=\"M9.5 2.5h4v4M13.5 2.5L9 7M6.5 13.5h-4v-4M2.5 13.5L7 9\"/>",   // focus a compact tile
        ["collapse"] = "<path d=\"M13.5 2.5L9 7M9 3.5V7h3.5M2.5 13.5L7 9M7 12.5V9H3.5\"/>",   // minimise the open tile (narrow windows)
        ["up"] = "<path d=\"M4 10l4-4 4 4\"/>",
        ["down"] = "<path d=\"M4 6l4 4 4-4\"/>",
        ["chart"] = "<path d=\"M2 13.5h12\"/><rect x=\"3.2\" y=\"8\" width=\"2.6\" height=\"5.5\" rx=\".7\"/><rect x=\"6.7\" y=\"3\" width=\"2.6\" height=\"10.5\" rx=\".7\"/><rect x=\"10.2\" y=\"6\" width=\"2.6\" height=\"7.5\" rx=\".7\"/>",
        ["doc"] = "<path d=\"M3.5 1.5h6L12.5 4.5v10h-9zM9.5 1.5v3h3M5.5 8h5M5.5 11h5\"/>",   // a page: the run's log
        ["trash"] = "<path d=\"M2.5 4h11M6 4V2.5h4V4M4 4l.7 9.5h6.6L12 4\"/>",
    };

    /// <summary>
    /// Markdown → HTML for display. Raw HTML in the file is not passed through, and the text of the board is never trusted for its links: a link is live only
    /// when it goes to http, https, mailto, or is relative (the app's own #wi / #n / [[doc]] links are made after the render, as relative ones); any other
    /// address (javascript:, data:, file:, \\host\share, //host) is plain text. An image is an image only when this app itself inlined a file of the repository
    /// (<see cref="ImageSrc"/>); any other image is a link to its address with its alt text, so rendering a note never fetches anything.
    /// </summary>
    public static readonly Markdig.MarkdownPipeline Pipeline = new Markdig.MarkdownPipelineBuilder().DisableHtml().UseEmphasisExtras().UseTaskLists().UsePipeTables().UseAutoLinks().Use<SafeLinks>().Build();   // plans carry tables and bare links

    /// <summary>The marker on a link whose image this app inlined from a file of the repository: the only image that is rendered as one.</summary>
    private static readonly object InlinedImage = new();

    private sealed class SafeLinks : Markdig.IMarkdownExtension
    {
        public void Setup(Markdig.MarkdownPipelineBuilder pipeline) { }

        public void Setup(Markdig.MarkdownPipeline pipeline, Markdig.Renderers.IMarkdownRenderer renderer)
        {
            if (renderer is Markdig.Renderers.HtmlRenderer html) html.ObjectRenderers.ReplaceOrAdd<Markdig.Renderers.Html.Inlines.LinkInlineRenderer>(new SafeLinkRenderer());
        }
    }

    private sealed class SafeLinkRenderer : Markdig.Renderers.Html.Inlines.LinkInlineRenderer
    {
        protected override void Write(Markdig.Renderers.HtmlRenderer renderer, Markdig.Syntax.Inlines.LinkInline link)
        {
            string url = link.Url ?? "";
            if (link.IsImage)
            {
                if (link.GetData(InlinedImage) is true) { base.Write(renderer, link); return; }
                if (!IsLiveAddress(url)) { renderer.WriteChildren(link); return; }       // the alt text
                renderer.Write("<a href=\"").WriteEscapeUrl(url).Write("\">");
                renderer.WriteChildren(link);
                renderer.Write("</a>");
                return;
            }
            if (IsLiveAddress(url)) base.Write(renderer, link); else renderer.WriteChildren(link);
        }
    }

    /// <summary>http, https, mailto, or a relative address (a path, a #fragment, item/12); never another scheme, a drive path, a UNC path or a protocol-relative one.</summary>
    internal static bool IsLiveAddress(string url)
    {
        url = url.Trim();
        if (url.Length == 0) return false;
        if (url.StartsWith("//", StringComparison.Ordinal) || url.StartsWith('\\') || url.Contains('\0') || url.Any(char.IsControl)) return false;
        int colon = url.IndexOf(':');
        int slash = url.IndexOfAny(['/', '?', '#']);
        if (colon < 0 || (slash >= 0 && slash < colon)) return true;   // relative: no scheme before the first / ? or #
        string scheme = url[..colon];
        return scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || scheme.Equals("https", StringComparison.OrdinalIgnoreCase) || scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The largest image file that is inlined into a page as a data URL.</summary>
    public const int MaxImageBytes = 8 * 1024 * 1024;

    /// <summary>Markdown → HTML, with the references written in it (#wiN, #nN, [[doc]]) linked: <see cref="LinkReferences"/>.</summary>
    public static MarkupString Markdown(string text) => new(LinkReferences(Markdig.Markdown.ToHtml(text, Pipeline), text));

    /// <summary>
    /// The one place a reference becomes a link, for every page that shows markdown: #wiN goes to item/N, #nN to note/N, and [[doc]] is marked as a document; one that points to nothing is struck through (class "bad").
    /// The grammar is OpenSpecStore's, whether it points to anything is Notes' (<see cref="NotesApi.ResolveLinks"/>). Text inside code and inside a link is left as written, and no attribute is touched.
    /// </summary>
    private static string LinkReferences(string html, string markdown)
    {
        if (OpenSpecStoreApi.FindReferences(markdown).Count == 0) return html;
        HashSet<string> exists = [.. NotesApi.ResolveLinks(markdown).Where(l => l.Resolved).Select(l => l.Kind + ":" + l.Target)];
        System.Text.StringBuilder result = new(html.Length + 64);
        int skipping = 0, at = 0;
        while (at < html.Length)
        {
            int open = html.IndexOf('<', at);
            string text = html[at..(open < 0 ? html.Length : open)];
            result.Append(skipping > 0 ? text : OpenSpecStoreApi.RewriteReferences(text, r => LinkOf(r, exists)));
            if (open < 0) break;
            int close = html.IndexOf('>', open);
            string tag = html[open..(close < 0 ? html.Length : close + 1)];
            result.Append(tag);
            string name = tag.TrimStart('<', '/').Split([' ', '>', '/'], 2)[0].ToLowerInvariant();
            if (name is "code" or "pre" or "a") skipping += tag.StartsWith("</", StringComparison.Ordinal) ? -1 : 1;
            skipping = Math.Max(skipping, 0);
            at = close < 0 ? html.Length : close + 1;
        }
        return result.ToString();
    }

    private static string LinkOf(OpenSpecStore.PublicDTOs.Reference r, HashSet<string> exists)
    {
        string target = System.Net.WebUtility.HtmlDecode(r.Target);
        bool ok = exists.Contains(r.Kind + ":" + target);
        string shown = System.Net.WebUtility.HtmlEncode(OpenSpecStoreApi.FormatReference(r.Kind, target));
        if (r.Kind == "doc") return $"<span class=\"wl{(ok ? "" : " bad")}\">{System.Net.WebUtility.HtmlEncode(target)}</span>";
        return ok ? $"<a class=\"wl\" href=\"{(r.Kind == "item" ? "item" : "note")}/{r.Number}\">{shown}</a>" : $"<span class=\"wl bad\">{shown}</span>";
    }

    private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    { [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif", [".webp"] = "image/webp", [".svg"] = "image/svg+xml" };

    /// <summary>Whether the file name has an image extension (the one table of them: png, jpg, jpeg, gif, webp, svg).</summary>
    public static bool IsImage(string entry) => ImageTypes.ContainsKey(System.IO.Path.GetExtension(entry));

    /// <summary>A repository-relative image file as a data URL the WebView can load, or null (not an image, outside the repository, missing).</summary>
    public static string? ImageSrc(string repoRoot, string entry)
    {
        entry = Uri.UnescapeDataString(entry ?? "");
        if (entry.Length == 0 || entry.Contains("..") || entry.Contains(':') || entry.StartsWith('/') || entry.StartsWith('\\')) return null;
        if (!ImageTypes.TryGetValue(System.IO.Path.GetExtension(entry), out string? type)) return null;
        string file = System.IO.Path.Combine(repoRoot, entry.Replace('/', System.IO.Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(file) || !StaysInside(repoRoot, entry) || new System.IO.FileInfo(file).Length > MaxImageBytes) return null;
        return $"data:{type};base64,{Convert.ToBase64String(System.IO.File.ReadAllBytes(file))}";
    }

    /// <summary>False when a folder or the file itself on the way from the repository root is a link (junction, symlink) that leads out of the repository.</summary>
    private static bool StaysInside(string repoRoot, string entry)
    {
        try
        {
            string root = System.IO.Path.GetFullPath(repoRoot).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            string current = root;
            foreach (string segment in entry.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                current = System.IO.Path.Combine(current, segment);
                System.IO.FileSystemInfo info = System.IO.Directory.Exists(current) ? new System.IO.DirectoryInfo(current) : new System.IO.FileInfo(current);
                if (!info.Exists || !info.Attributes.HasFlag(System.IO.FileAttributes.ReparsePoint)) continue;
                string? target = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
                if (target is null || !(target.TrimEnd(System.IO.Path.DirectorySeparatorChar).Equals(root, StringComparison.OrdinalIgnoreCase) || target.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) return false;
            }
            return true;
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    /// <summary>
    /// Markdown → HTML where an image link to a file of the repository (<c>![](local/…/pasted.png)</c>) shows that file. With <paramref name="folder"/>
    /// (the repository-relative folder of the document being shown) a link is read as a file beside the document first, then from the repository root.
    /// </summary>
    public static MarkupString Markdown(string text, string? repoRoot, string? folder = null) => WithReferences(MarkdownHtml(text, repoRoot, folder), text);

    /// <summary>The markdown converted, before its references are linked (the part that costs: a caller that shows the same text again keeps this and links again, because what a reference points to can change).</summary>
    public static string MarkdownHtml(string text, string? repoRoot, string? folder = null)
    {
        if (repoRoot is null || !text.Contains("![", StringComparison.Ordinal)) return Markdig.Markdown.ToHtml(text, Pipeline);
        Markdig.Syntax.MarkdownDocument doc = Markdig.Markdown.Parse(text, Pipeline);
        foreach (Markdig.Syntax.Inlines.LinkInline link in Markdig.Syntax.MarkdownObjectExtensions.Descendants<Markdig.Syntax.Inlines.LinkInline>(doc).Where(l => l.IsImage && l.Url is not null))
            if (((string.IsNullOrEmpty(folder) ? null : ImageSrc(repoRoot, folder + "/" + link.Url)) ?? ImageSrc(repoRoot, link.Url!)) is string src) { link.Url = src; link.SetData(InlinedImage, true); }
        System.IO.StringWriter html = new();
        Markdig.Renderers.HtmlRenderer renderer = new(html);
        Pipeline.Setup(renderer);
        renderer.Render(doc);
        return html.ToString();
    }

    /// <summary>The html of <see cref="MarkdownHtml"/> with the references written in its <paramref name="markdown"/> linked.</summary>
    public static MarkupString WithReferences(string html, string markdown) => new(LinkReferences(html, markdown));

    public static MarkupString Icon(string name) => new(
        $"<svg viewBox=\"0 0 16 16\" width=\"14\" height=\"14\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">{Paths[name]}</svg>");
}
