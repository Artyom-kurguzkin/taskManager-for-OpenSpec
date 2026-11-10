using System.Text.RegularExpressions;
using OpenSpecStore;
using YamlDotNet.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Serialization.NamingConventions;

namespace WorkItems.Infrastructure;

/// <summary>The app's <c>x-rtm:</c> block in a change's .openspec.yaml. Property order is the order written.</summary>
internal sealed class Rtm
{
    public int? Id { get; set; }
    public string? Title { get; set; }
    public string? Status { get; set; }
    public string? Priority { get; set; }
    /// <summary>Written even when empty: <c>[]</c> means "nobody", while a missing list lets git commit authors fill in.</summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public List<string>? Contributors { get; set; }
    public List<string>? Features { get; set; }
    public Dictionary<string, string>? Lanes { get; set; }
    public string? Label { get; set; }
    /// <summary>Days as yyyy-MM-dd: when work started (stamped the first time the item moves to In Progress) and when it is due.</summary>
    public string? Started { get; set; }
    public string? Due { get; set; }
    /// <summary>yyyy-MM-dd: the day the item last moved to its current status (stamped on creation and on every status change).</summary>
    public string? Moved { get; set; }
    /// <summary>A plan file for this item: a repo-relative path with forward slashes, like DevResources/plans/name.md.</summary>
    public string? Plan { get; set; }
    /// <summary>"Deleted" in the app: hidden from the board, folder kept. Null (omitted) when visible.</summary>
    public bool? Hidden { get; set; }
    public List<string>? Commits { get; set; }
    public List<RtmTest>? Tests { get; set; }
    /// <summary>Documents linked to this item: repo-relative paths with forward slashes.</summary>
    public List<string>? Docs { get; set; }
    /// <summary>Ids of work items that must be done before this one can leave Blocked.</summary>
    public List<int>? DependsOn { get; set; }
    /// <summary>Numbers of the work items this one was merged from: git commits mentioning them still belong here.</summary>
    public List<int>? MergedFrom { get; set; }
    /// <summary>Features from the change's specs/ folder that the user removed from this item (the specs stay).</summary>
    public List<string>? UnlinkedFeatures { get; set; }
    /// <summary>Written by an agent that created or changed this change: its name and the time (yyyy-MM-ddTHH:mm:ss).</summary>
    public string? AgentBy { get; set; }
    public string? AgentAt { get; set; }
    /// <summary>Written by the app when the user marks the agent's work as reviewed.</summary>
    public string? ReviewedAt { get; set; }
}

internal sealed class RtmTest
{
    public string Suite { get; set; } = "";
    public string Test { get; set; } = "";
    public int? Note { get; set; }
}

internal sealed class MetadataException(string message) : Exception(message);

internal static class Metadata
{
    private static readonly IDeserializer Reader = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance).IgnoreUnmatchedProperties().Build();

    /// <summary>Lists are written as [a, b] (as people write them by hand), so a change shows as a one-line git diff; mappings inside a list ({suite: x, test: y}) go on that line too.
    /// Strings are quoted when YAML would read them as something else (see <see cref="OpenSpecStoreApi.YamlWriter"/>).</summary>
    private static readonly ISerializer Writer = OpenSpecStoreApi.YamlWriter(model: true, flowLists: true);

    /// <summary>The x-rtm block, or an empty value when there is none. Throws <see cref="MetadataException"/> when the text is not valid YAML or does not fit: callers show it, and never write over such a file.</summary>
    internal static T Read<T>(string yamlPath) where T : class, new() => Read<T>(yamlPath, out _);

    /// <summary>
    /// Like <see cref="Read{T}"/>. A value that was hand-written without quotes and is not valid YAML (<c>title: Fix: the thing</c>) is read as the text it was meant to be:
    /// <paramref name="mended"/> then says which lines, and the next write of the block writes them quoted. Null when nothing needed mending.
    /// </summary>
    internal static T Read<T>(string yamlPath, out string? mended) where T : class, new()
    {
        mended = null;
        if (OpenSpecStoreApi.ReadRtmBlock(yamlPath) is not string block || block.Trim().Length == 0) return new T();
        return Parse<T>(yamlPath, block, BlockOffset(yamlPath), out mended) ?? new T();
    }

    /// <summary>Like <see cref="Read{T}"/>, but a file that does not parse gives an empty value and its message, for steps that only look.</summary>
    internal static T TryRead<T>(string yamlPath, out string? problem) where T : class, new()
    {
        try { problem = null; return Read<T>(yamlPath); }
        catch (MetadataException e) { problem = e.Message; return new T(); }
    }

    internal static (bool Success, string Message) Write<T>(string yamlPath, T value) =>
        OpenSpecStoreApi.WriteRtmBlock(yamlPath, Writer.Serialize(value!));

    /// <summary>One section of the x-rtm block (config.yaml is shared with other modules, so never rewrite the whole block).</summary>
    internal static T? ReadSection<T>(string yamlPath, string key) where T : class =>
        OpenSpecStoreApi.ReadRtmSection(yamlPath, key) is string s && s.Trim().Length > 0 ? Parse<T>(yamlPath, s, null) : null;

    /// <summary>
    /// Parses a block of the file. Line endings are made the same first (a hand edit or a tool can leave a file with both).
    /// <paramref name="offset"/> = how many lines of the file come before the block's first line, so a failure names the file's line.
    /// </summary>
    private static T? Parse<T>(string yamlPath, string text, int? offset) where T : class => Parse<T>(yamlPath, text, offset, out _);

    private static T? Parse<T>(string yamlPath, string text, int? offset, out string? mended) where T : class
    {
        mended = null;
        string clean = text.Replace("\r\n", "\n").Replace('\r', '\n');
        try { return Reader.Deserialize<T>(clean); }
        catch (YamlException e)
        {
            // one mending pass: an unquoted value like "title: Fix: the thing" is read as the text it was meant to be
            (string fixedText, IReadOnlyList<(string Key, int Line)> lines) = OpenSpecStoreApi.MendYaml(clean, TextKeys<T>());
            if (lines.Count > 0)
            {
                try
                {
                    T? value = Reader.Deserialize<T>(fixedText);
                    mended = string.Join("; ", lines.Select(l => $"{l.Key}{(offset is int off ? $" on line {off + l.Line}" : "")} was not valid YAML and was read as text")) + "; the file is fixed on the next save";
                    return value;
                }
                catch (Exception again) when (again is YamlException or FormatException or InvalidCastException or InvalidOperationException) { }   // not what was wrong: report the first failure
            }
            string reason = (e.InnerException ?? e).Message;
            reason = Regex.Replace(reason, @"^\(Line:[^)]*\)\s*-\s*\(Line:[^)]*\):\s*", "").TrimEnd('.');
            string where = offset is int o && e.Start.Line > 0 ? $" (line {o + e.Start.Line}: {Lower(reason)})" : $" ({Lower(reason)})";
            throw new MetadataException($"{Path.GetFileName(yamlPath)} could not be read{where}");
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or InvalidOperationException)
        {
            throw new MetadataException($"{Path.GetFileName(yamlPath)} could not be read ({Lower(e.Message.TrimEnd('.'))})");
        }
    }

    /// <summary>The keys of <typeparamref name="T"/> whose value is text (title, status, label, plan, …), as they are spelled in the file.</summary>
    private static IReadOnlySet<string> TextKeys<T>() => typeof(T).GetProperties()
        .Where(p => p.PropertyType == typeof(string) && p.GetCustomAttributes(typeof(YamlIgnoreAttribute), true).Length == 0)
        .Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..]).ToHashSet();

    private static string Lower(string s) => s.Length > 1 && char.IsUpper(s[0]) && char.IsLower(s[1]) ? char.ToLowerInvariant(s[0]) + s[1..] : s;

    /// <summary>How many lines of the file precede the x-rtm block's first line (its key's line, when the value is inline).</summary>
    private static int BlockOffset(string yamlPath)
    {
        try
        {
            string[] lines = File.ReadAllText(yamlPath).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int key = Array.FindIndex(lines, l => l.StartsWith("x-rtm:", StringComparison.Ordinal));
            return key < 0 ? 0 : lines[key]["x-rtm:".Length..].Trim().Length > 0 ? key : key + 1;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }

    internal static (bool Success, string Message) WriteSection<T>(string yamlPath, string key, T? value) where T : class =>
        OpenSpecStoreApi.WriteRtmSection(yamlPath, key, value is null ? null : Writer.Serialize(value));
}
