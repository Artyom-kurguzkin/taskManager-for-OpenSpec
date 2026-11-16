using Markdig;
using Microsoft.AspNetCore.Components;
using WorkItems;

namespace TaskManager.Components;

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

    public static string PriorityColor(string priority) => priority == "high" ? "--peach" : "--stone";

    /// <summary>The choices a Pill / Dropdown offers for each work item field, as (value, label).</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> PriorityOptions = [("high", "high"), ("low", "lower")];
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

    public static MarkupString Markdown(string text) => new(Markdig.Markdown.ToHtml(text, Pipeline));

    private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    { [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif", [".webp"] = "image/webp", [".svg"] = "image/svg+xml" };

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
    public static MarkupString Markdown(string text, string? repoRoot, string? folder = null)
    {
        if (repoRoot is null || !text.Contains("![", StringComparison.Ordinal)) return Markdown(text);
        Markdig.Syntax.MarkdownDocument doc = Markdig.Markdown.Parse(text, Pipeline);
        foreach (Markdig.Syntax.Inlines.LinkInline link in Markdig.Syntax.MarkdownObjectExtensions.Descendants<Markdig.Syntax.Inlines.LinkInline>(doc).Where(l => l.IsImage && l.Url is not null))
            if (((string.IsNullOrEmpty(folder) ? null : ImageSrc(repoRoot, folder + "/" + link.Url)) ?? ImageSrc(repoRoot, link.Url!)) is string src) { link.Url = src; link.SetData(InlinedImage, true); }
        System.IO.StringWriter html = new();
        Markdig.Renderers.HtmlRenderer renderer = new(html);
        Pipeline.Setup(renderer);
        renderer.Render(doc);
        return new(html.ToString());
    }

    public static MarkupString Icon(string name) => new(
        $"<svg viewBox=\"0 0 16 16\" width=\"14\" height=\"14\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">{Paths[name]}</svg>");
}
