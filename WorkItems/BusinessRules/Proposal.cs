using System.Text.RegularExpressions;

namespace WorkItems.BusinessRules;

/// <summary>proposal.md: a first-level heading, then the body that the app shows as the description.</summary>
internal static partial class Proposal
{
    internal const string TemplateHeading = "Proposal";

    [GeneratedRegex(@"^# (?<h>.+)$", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^### Requirement:\s*(?<r>.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex Requirement();

    /// <summary>The proposal's own heading, unless it is the OpenSpec template's generic "# Proposal".</summary>
    internal static string? Title(string text) =>
        Heading().Match(text) is { Success: true } m && m.Groups["h"].Value.Trim() is string h && h != TemplateHeading ? h : null;

    internal static string Body(string text)
    {
        Match m = Heading().Match(text);
        string body = m.Success ? text[(m.Index + m.Length)..] : text;
        return body.Replace("\r\n", "\n").TrimStart('\n');
    }

    internal static string WithBody(string text, string body)
    {
        Match m = Heading().Match(text);
        string heading = m.Success ? m.Value.TrimEnd('\r') : $"# {TemplateHeading}";
        return $"{heading}\n\n{body.Replace("\r\n", "\n").TrimStart('\n')}";
    }

    internal static IEnumerable<string> RequirementNames(string deltaSpec) => Requirement().Matches(deltaSpec).Select(m => m.Groups["r"].Value);

    /// <summary>"add-health-check" or "2026-09-18-session-timeout" → "Add health check" / "Session timeout".</summary>
    internal static string FolderTitle(string folder)
    {
        string name = ArchivePrefix().Replace(folder, "").Replace('-', ' ');
        return name.Length == 0 ? folder : char.ToUpperInvariant(name[0]) + name[1..];
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}-")]
    internal static partial Regex ArchivePrefix();

    /// <summary>The folder name for a title: its first words, at most 6 and 48 characters, cut at a word boundary.</summary>
    internal static string Slug(string title)
    {
        string slug = "";
        foreach (string word in SlugParts().Matches(title.ToLowerInvariant()).Select(m => m.Value).Take(6))
        {
            string next = slug.Length == 0 ? word : $"{slug}-{word}";
            if (next.Length > 48) return slug.Length == 0 ? word[..48] : slug;
            slug = next;
        }
        return slug.Length > 0 ? slug : "work-item";
    }

    [GeneratedRegex("[a-z0-9]+")]
    private static partial Regex SlugParts();
}
