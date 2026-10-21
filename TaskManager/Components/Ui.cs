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
        "In Review" => "--mint",
        "Completed" => "--sage",
        _ => "--stone",
    };

    public static string PriorityColor(string priority) => priority == "high" ? "--peach" : "--stone";

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
        ["eye"] = "<path d=\"M1.5 8S3.9 3.5 8 3.5 14.5 8 14.5 8 12.1 12.5 8 12.5 1.5 8 1.5 8z\"/><circle cx=\"8\" cy=\"8\" r=\"2\"/>",
        ["eyeoff"] = "<path d=\"M1.5 8S3.9 3.5 8 3.5 14.5 8 14.5 8 12.1 12.5 8 12.5 1.5 8 1.5 8zM2.5 13.5l11-11\"/>",
        ["up"] = "<path d=\"M4 10l4-4 4 4\"/>",
        ["down"] = "<path d=\"M4 6l4 4 4-4\"/>",
        ["trash"] = "<path d=\"M2.5 4h11M6 4V2.5h4V4M4 4l.7 9.5h6.6L12 4\"/>",
    };

    /// <summary>Markdown → HTML for display. Raw HTML in the file is not passed through.</summary>
    public static readonly Markdig.MarkdownPipeline Pipeline = new Markdig.MarkdownPipelineBuilder().DisableHtml().Build();

    public static MarkupString Markdown(string text) => new(Markdig.Markdown.ToHtml(text, Pipeline));

    public static MarkupString Icon(string name) => new(
        $"<svg viewBox=\"0 0 16 16\" width=\"14\" height=\"14\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">{Paths[name]}</svg>");
}
