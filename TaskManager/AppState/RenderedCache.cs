using Microsoft.AspNetCore.Components;
using OpenSpecStore;
using TaskManager.Components;
using TaskManager.Helpers;

namespace TaskManager.AppState;

/// <summary>
/// What is worked out from text and files for drawing: a markdown text as HTML (by text and repository), the links a text holds (by text), and an attached image as a data URL
/// (by repository and entry, checked against the file's size and write time when the board's content moved). A text rarely changes between two draws, so none of it is worked out twice.
/// </summary>
public static class RenderedCache
{
    private const int Limit = 500;

    internal sealed class State
    {
        public readonly Dictionary<(string? Root, string Text), string> Html = [];
        public readonly Dictionary<string, IReadOnlyList<(string Url, bool IsImage)>> Links = [];
        public readonly Dictionary<(string Root, string Entry), (long Version, long Length, long Ticks, string? Url)> Images = [];
        public int HtmlRenders, ImageReads;
    }

    /// <summary>How many conversions of markdown to HTML were made, and how many image files were read (a test counts them).</summary>
    public static int HtmlRenders => Caches.S.Rendered.HtmlRenders;
    public static int ImageReads => Caches.S.Rendered.ImageReads;

    /// <summary>The markdown as HTML, converted once per text and repository. What the references in it point to is worked out again each time (<see cref="Ui.WithReferences"/>): it can change without the text changing.</summary>
    public static string Html(string markdown, string? root) => Html(markdown, root, out _);

    internal static string Html(string markdown, string? root, out bool converted)
    {
        State s = Caches.S.Rendered;
        lock (s.Html) if (s.Html.TryGetValue((root, markdown), out string? hit)) { converted = false; return hit; }
        string html = Ui.MarkdownHtml(markdown, root);   // outside the lock: a conversion blocks nobody else's hit
        lock (s.Html)
        {
            if (s.Html.Count >= Limit) s.Html.Clear();
            s.Html[(root, markdown)] = html;
            s.HtmlRenders++;
        }
        converted = true;
        return html;
    }

    /// <summary>The links (address, is it an image) a markdown text holds, parsed once per text.</summary>
    public static IReadOnlyList<(string Url, bool IsImage)> Links(string text)
    {
        State s = Caches.S.Rendered;
        lock (s.Links)
        {
            if (s.Links.TryGetValue(text, out var hit)) return hit;
            IReadOnlyList<(string, bool)> links = [.. Markdig.Syntax.MarkdownObjectExtensions.Descendants<Markdig.Syntax.Inlines.LinkInline>(Markdig.Markdown.Parse(text, Ui.Pipeline)).Select(l => (l.Url ?? "", l.IsImage))];
            if (s.Links.Count >= Limit) s.Links.Clear();
            return s.Links[text] = links;
        }
    }

    /// <summary>The image as a data URL, or null when it is missing, too large, or not inside the repository. The file is read again only when its size or write time moved (looked at once per content version of the board).</summary>
    public static string? Image(string root, string entry)
    {
        State s = Caches.S.Rendered;
        long version = OpenSpecStoreApi.Version;
        string full = System.IO.Path.Combine(root, entry.Replace('/', System.IO.Path.DirectorySeparatorChar));
        lock (s.Images)
        {
            if (s.Images.TryGetValue((root, entry), out var hit) && hit.Version == version) return hit.Url;
            FileInfo? file = entry.Contains("..") ? null : new FileInfo(full);
            (long length, long ticks) = file is { Exists: true } ? (file.Length, file.LastWriteTimeUtc.Ticks) : (-1, 0);
            if (s.Images.TryGetValue((root, entry), out hit) && hit.Length == length && hit.Ticks == ticks) { s.Images[(root, entry)] = hit with { Version = version }; return hit.Url; }
            string? url = Ui.ImageSrc(root, entry);
            s.ImageReads++;
            if (s.Images.Count >= Limit) s.Images.Clear();
            s.Images[(root, entry)] = (version, length, ticks, url);
            return url;
        }
    }

    public static void Invalidate()
    {
        State s = Caches.S.Rendered;
        lock (s.Html) s.Html.Clear();
        lock (s.Links) s.Links.Clear();
        lock (s.Images) s.Images.Clear();
    }
}

/// <summary>The markdown of a work item's notes as HTML, for as long as its owner lives (the item page): a reload after a tick, a link or a status change converts only the notes whose text changed.</summary>
public sealed class NoteHtmlCache
{
    /// <summary>How many conversions of markdown to HTML this owner caused (a test counts them).</summary>
    public int Renders { get; private set; }

    public MarkupString Of(int note, string body, string? root)
    {
        string html = RenderedCache.Html(body, root, out bool converted);
        if (converted) Renders++;
        return Ui.WithReferences(html, body);
    }
}
