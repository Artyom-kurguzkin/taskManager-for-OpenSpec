using Markdig;
using Microsoft.AspNetCore.Components;

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
        "Completed" => "--sage",
        _ => "--stone",
    };

    private static readonly Dictionary<string, string> Paths = new()
    {
        ["cal"] = "<rect x=\"2\" y=\"3\" width=\"12\" height=\"11\" rx=\"2\"/><path d=\"M2 7h12M5 1.5v3M11 1.5v3\"/>",
        ["list"] = "<path d=\"M6 4h8M6 8h8M6 12h8M2.5 4h.5M2.5 8h.5M2.5 12h.5\"/>",
        ["user"] = "<circle cx=\"8\" cy=\"5.5\" r=\"2.8\"/><path d=\"M2.5 14c.8-3 3-4.5 5.5-4.5s4.7 1.5 5.5 4.5\"/>",
        ["chev"] = "<path d=\"M6 4l4 4-4 4\"/>",
        ["caret"] = "<path d=\"M4 6l4 4 4-4\"/>",
    };

    /// <summary>Markdown → HTML for display. Raw HTML in the file is not passed through.</summary>
    public static readonly Markdig.MarkdownPipeline Pipeline = new Markdig.MarkdownPipelineBuilder().DisableHtml().Build();

    public static MarkupString Markdown(string text) => new(Markdig.Markdown.ToHtml(text, Pipeline));

    public static MarkupString Icon(string name) => new(
        $"<svg viewBox=\"0 0 16 16\" width=\"14\" height=\"14\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">{Paths[name]}</svg>");
}
