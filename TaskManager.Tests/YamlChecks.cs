using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace TaskManager.Tests;

/// <summary>What every other YAML reader (the OpenSpec CLI's, an editor's) would make of a file the app wrote.</summary>
internal static partial class YamlChecks
{
    /// <summary>Text that a YAML parser reads as a boolean, a null or a number when it is plain.</summary>
    [GeneratedRegex(@"^(true|false|yes|no|on|off|y|n|null|~|[-+]?(\d[\d_]*\.?\d*|\.\d+)([eE][-+]?\d+)?|0x[0-9a-fA-F]+|0o[0-7]+|[-+]?\.(inf|nan))$", RegexOptions.IgnoreCase)]
    private static partial Regex Ambiguous();

    /// <summary>Strings YAML has a special use for: what the app writes of these must read back as the same text, in every reader.</summary>
    internal static readonly string[] Nasty =
    [
        "Fix: the thing #1", "# starts with a hash", "- starts with a dash", "* star", "&anchor", "!tag", "[bracket] first", "{brace} first",
        "\"double\" quoted", "'single' quoted", "it's \"both\" kinds", "yes", "No", "on", "OFF", "null", "~", "true", "123", "1.5", "1e34567", "0x1F", "2026-10-01",
        "a, b", "a: b", "key:", "a #hash b", "a#b", "x]y", "p{q}", "@at", "`tick`", "%percent", "| pipe", "> gt", "? q", "tab\there", "back\\slash", "ünï ✓", "two\nlines",
        "A fairly long title that is certain to run past the width at which a YAML writer folds its lines: and then keeps going until the end #7",
    ];

    /// <summary>The file must be valid YAML for a strict parser, and no plain scalar in it may be one of <paramref name="ours"/> that would read as something other than text.</summary>
    internal static void Valid(string yaml, IEnumerable<string> ours, string because = "")
    {
        YamlStream stream = new();
        try { stream.Load(new StringReader(yaml.Replace("\r\n", "\n"))); }
        catch (YamlException e) { throw new Xunit.Sdk.XunitException($"Not valid YAML ({e.Message}) {because}\n{yaml}"); }
        HashSet<string> mine = [.. ours];
        foreach (YamlDocument doc in stream.Documents) Walk(doc.RootNode, mine, because, yaml);
    }

    /// <summary>Keys the app writes real booleans and numbers under: <c>skip_specs: true</c>, <c>hidden: true</c>, <c>id: 123</c>.</summary>
    private static readonly string[] BooleanKeys = ["skip_specs", "hidden", "id", "note", "dependsOn", "mergedFrom"];

    private static void Walk(YamlNode node, HashSet<string> mine, string because, string yaml)
    {
        switch (node)
        {
            case YamlScalarNode { Style: ScalarStyle.Plain } s when s.Value is { } v && mine.Contains(v) && Ambiguous().IsMatch(v):
                throw new Xunit.Sdk.XunitException($"'{v}' is written plain, so another YAML reader takes it for a boolean, a null or a number {because}\n{yaml}");
            case YamlMappingNode map:
                foreach ((YamlNode key, YamlNode value) in map.Children)
                {
                    Walk(key, mine, because, yaml);
                    if (key is not YamlScalarNode { Value: { } k } || !BooleanKeys.Contains(k)) Walk(value, mine, because, yaml);
                }
                break;
            case YamlSequenceNode seq:
                foreach (YamlNode child in seq.Children) Walk(child, mine, because, yaml);
                break;
        }
    }
}
