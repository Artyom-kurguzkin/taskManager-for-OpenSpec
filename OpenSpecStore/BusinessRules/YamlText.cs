using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenSpecStore;

/// <summary>
/// The one place the app's YAML is written and mended. Written: a string the app puts in a file is quoted when YAML would otherwise
/// read it as something else. YamlDotNet's emitter already quotes what has a colon, a hash, a leading indicator, a line break or
/// surrounding spaces (and keeps its existing choice of quote, so a file's lines do not change for nothing); what it lets through plain
/// are the words YAML reads as a boolean, null or a number (yes, off, null, 123, 1e5), and this writer double-quotes those.
/// Plain words stay plain, so files stay readable and diffs small. Mended: a hand-written <c>key: value</c> line that is not valid YAML
/// (<c>title: Fix: the thing</c>) is read as the text it was meant to be.
/// </summary>
internal static partial class YamlText
{
    /// <summary>Words and numbers a YAML reader turns into booleans, null or numbers when they are plain (YAML 1.1 and 1.2 spellings).</summary>
    [GeneratedRegex(@"^(true|false|yes|no|on|off|y|n|null|~|[-+]?(\d[\d_]*(\.\d*)?|\.\d+)([eE][-+]?\d+)?|0[xob][0-9a-fA-F_]+|[-+]?\.(inf|nan))$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NotText();

    /// <summary>
    /// A serializer for the app's files. <paramref name="model"/>: property names in camelCase and nulls and empty lists left out (the x-rtm block and note frontmatter).
    /// <paramref name="flowLists"/>: lists as [a, b] on one line.
    /// </summary>
    internal static ISerializer Writer(bool model, bool flowLists)
    {
        SerializerBuilder builder = new SerializerBuilder().WithEventEmitter(next => new Quoting(next, flowLists));
        if (model) builder = builder.WithNamingConvention(CamelCaseNamingConvention.Instance).ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections);
        return builder.Build();
    }

    /// <summary>Quotes the words that would read as something else, and (when asked) puts lists, and mappings inside lists, on one line.</summary>
    private sealed class Quoting(IEventEmitter next, bool flowLists) : ChainedEventEmitter(next)
    {
        private readonly Stack<bool> inFlow = new();   // for each collection we are in: is it, or is something around it, in flow style
        private bool Flow => inFlow.Count > 0 && inFlow.Peek();

        public override void Emit(SequenceStartEventInfo eventInfo, IEmitter emitter)
        {
            if (flowLists) eventInfo.Style = SequenceStyle.Flow;
            inFlow.Push(Flow || eventInfo.Style == SequenceStyle.Flow);
            base.Emit(eventInfo, emitter);
        }

        public override void Emit(SequenceEndEventInfo eventInfo, IEmitter emitter)
        {
            inFlow.Pop();
            base.Emit(eventInfo, emitter);
        }

        public override void Emit(MappingStartEventInfo eventInfo, IEmitter emitter)
        {
            if (flowLists && Flow) eventInfo.Style = MappingStyle.Flow;
            inFlow.Push(Flow || eventInfo.Style == MappingStyle.Flow);
            base.Emit(eventInfo, emitter);
        }

        public override void Emit(MappingEndEventInfo eventInfo, IEmitter emitter)
        {
            inFlow.Pop();
            base.Emit(eventInfo, emitter);
        }

        public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
        {
            if (eventInfo.Source.Value is string s && NotText().IsMatch(s)) eventInfo.Style = ScalarStyle.DoubleQuoted;
            base.Emit(eventInfo, emitter);
        }
    }

    [GeneratedRegex(@"^(?<key>[A-Za-z][A-Za-z0-9_-]*):[ \t]+(?<value>\S.*?)[ \t]*$")]
    private static partial Regex KeyValueLine();

    /// <summary>
    /// Mends a block of YAML (top-level keys at the left edge): a line <c>key: value</c> of one of <paramref name="keys"/> (keys whose value is text)
    /// whose value is not valid YAML on its own, and not a single plain, quoted or block text, gets the whole value quoted. Valid lines
    /// are never touched. Returns the new text and the 1-based line of each repaired line, with its key.
    /// </summary>
    internal static (string Text, IReadOnlyList<(string Key, int Line)> Repaired) Mend(string text, IReadOnlySet<string> keys)
    {
        string[] lines = text.Split('\n');
        List<(string Key, int Line)> repaired = [];
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (KeyValueLine().Match(line) is not { Success: true } m || !keys.Contains(m.Groups["key"].Value) || IsText(line)) continue;
            lines[i] = $"{m.Groups["key"].Value}: {Quote(m.Groups["value"].Value)}" + (lines[i].EndsWith('\r') ? "\r" : "");
            repaired.Add((m.Groups["key"].Value, i + 1));
        }
        return (string.Join('\n', lines), repaired);
    }

    /// <summary>True when the line alone is one key with a text value.</summary>
    private static bool IsText(string line)
    {
        try
        {
            YamlStream stream = new();
            stream.Load(new StringReader(line));
            return stream.Documents.Count == 1 && stream.Documents[0].RootNode is YamlMappingNode { Children.Count: 1 } map && map.Children[0].Value is YamlScalarNode;
        }
        catch (YamlException) { return false; }
    }

    /// <summary>The text as a double-quoted YAML scalar.</summary>
    internal static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\t", "\\t") + "\"";
}
