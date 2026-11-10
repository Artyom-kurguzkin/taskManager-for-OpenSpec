using System.Text.RegularExpressions;
using OpenSpecStore;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Notes.BusinessRules;

internal sealed class NoteMeta
{
    public int Id { get; set; }
    public string? By { get; set; }
    public string? Created { get; set; }
    public string? Title { get; set; }
    public List<string>? Links { get; set; }
    public List<string>? Commits { get; set; }
    public List<string>? Attachments { get; set; }
    public string? AgentBy { get; set; }
    public string? AgentAt { get; set; }
    public string? ReviewedAt { get; set; }
    public int? MergedFrom { get; set; }       // a merge moved this note in from that work item
    public string? MergedAt { get; set; }      // yyyy-MM-dd of that merge
    /// <summary>Set (never written) when the frontmatter did not parse: why. Such a note shows with its body and is not written to.</summary>
    [YamlIgnore] public string? Problem { get; set; }
}

/// <summary>A note file: a <c>---</c> frontmatter block, then the markdown body.</summary>
internal static class NoteFile
{
    private static readonly IDeserializer Reader = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance).IgnoreUnmatchedProperties().Build();

    /// <summary>Strings are quoted when YAML would read them as something else (a colon, a hash, yes, 123, …).</summary>
    private static readonly ISerializer Writer = OpenSpecStoreApi.YamlWriter(model: true, flowLists: false);

    private static readonly IReadOnlySet<string> TextKeys = typeof(NoteMeta).GetProperties()
        .Where(p => p.PropertyType == typeof(string) && p.GetCustomAttributes(typeof(YamlIgnoreAttribute), true).Length == 0)
        .Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..]).ToHashSet();

    /// <summary>Splits into (frontmatter including both --- lines, body).</summary>
    internal static (string Frontmatter, string Body) Split(string text)
    {
        string t = text.Replace("\r\n", "\n");
        if (!t.StartsWith("---\n")) return ("", t);
        int end = t.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        return end < 0 ? ("", t) : (t[..(end + 5)], t[(end + 5)..]);
    }

    /// <summary>The frontmatter's fields. One that does not parse gives an empty value with <see cref="NoteMeta.Problem"/> set (the file's line and the parser's reason), so the note still shows and is never written over.</summary>
    internal static NoteMeta Meta(string frontmatter)
    {
        if (frontmatter.Length <= 8) return new();
        string yaml = frontmatter[4..^5];
        try { return Reader.Deserialize<NoteMeta>(yaml) ?? new(); }
        catch (Exception e) when (e is YamlException or FormatException or InvalidCastException or InvalidOperationException)
        {
            // one mending pass: an unquoted value like "title: Ping: pong" is read as the text it was meant to be, and written quoted the next time the note is
            (string fixedYaml, IReadOnlyList<(string Key, int Line)> mended) = OpenSpecStoreApi.MendYaml(yaml, TextKeys);
            if (mended.Count > 0)
            {
                try { return Reader.Deserialize<NoteMeta>(fixedYaml) ?? new(); }
                catch (Exception again) when (again is YamlException or FormatException or InvalidCastException or InvalidOperationException) { }   // not what was wrong: report the first failure
            }
            string reason = (e.InnerException ?? e).Message;
            reason = Regex.Replace(reason, @"^\(Line:[^)]*\)\s*-\s*\(Line:[^)]*\):\s*", "").TrimEnd('.');
            if (reason.Length > 1 && char.IsUpper(reason[0]) && char.IsLower(reason[1])) reason = char.ToLowerInvariant(reason[0]) + reason[1..];
            string line = e is YamlException { Start.Line: > 0 } y ? $"line {y.Start.Line + 1}: " : "";   // +1: the opening --- line
            return new() { Problem = $"frontmatter could not be read ({line}{reason})" };
        }
    }

    internal static string Compose(NoteMeta meta, string body) => $"---\n{Writer.Serialize(meta).Replace("\r\n", "\n")}---\n{body.Replace("\r\n", "\n")}";

    /// <summary>The frontmatter <c>title:</c>; a note without one is titled by its first "# " heading (older notes), else by its number.</summary>
    internal static string Title(string? metaTitle, string body, int id) =>
        metaTitle?.Trim() is { Length: > 0 } t ? t
        : body.Split('\n').FirstOrDefault(l => l.StartsWith("# "))?[2..].Trim() is { Length: > 0 } h ? h : $"Note #n{id}";
}
