using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Notes.BusinessRules;

internal sealed class NoteMeta
{
    public int Id { get; set; }
    public string? By { get; set; }
    public string? Created { get; set; }
    public List<string>? Links { get; set; }
    public List<string>? Commits { get; set; }
}

/// <summary>A note file: a <c>---</c> frontmatter block, then the markdown body.</summary>
internal static class NoteFile
{
    private static readonly IDeserializer Reader = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance).IgnoreUnmatchedProperties().Build();

    private static readonly ISerializer Writer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
        .Build();

    /// <summary>Splits into (frontmatter including both --- lines, body).</summary>
    internal static (string Frontmatter, string Body) Split(string text)
    {
        string t = text.Replace("\r\n", "\n");
        if (!t.StartsWith("---\n")) return ("", t);
        int end = t.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        return end < 0 ? ("", t) : (t[..(end + 5)], t[(end + 5)..]);
    }

    internal static NoteMeta Meta(string frontmatter) =>
        frontmatter.Length > 8 ? Reader.Deserialize<NoteMeta>(frontmatter[4..^5]) ?? new() : new();

    internal static string Compose(NoteMeta meta, string body) => $"---\n{Writer.Serialize(meta).Replace("\r\n", "\n")}---\n{body.Replace("\r\n", "\n")}";

    internal static string Title(string body, int id) =>
        body.Split('\n').FirstOrDefault(l => l.StartsWith("# "))?[2..].Trim() is { Length: > 0 } h ? h : $"Note #n{id}";
}
