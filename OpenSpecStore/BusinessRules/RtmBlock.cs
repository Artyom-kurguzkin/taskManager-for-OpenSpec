namespace OpenSpecStore;

/// <summary>
/// Text-level handling of a top-level YAML key and everything indented under it. Used for the app's
/// <c>x-rtm:</c> block in a file and, one level down, for one section inside that block. Lines outside the
/// key are never touched, so comments and unknown keys survive without a YAML round-trip.
/// </summary>
internal static class RtmBlock
{
    internal const string Root = "x-rtm";

    internal static string? Extract(string yaml) => Extract(yaml, Root);

    internal static string Replace(string yaml, string blockYaml) => Replace(yaml, Root, blockYaml);

    /// <summary>The de-indented content under <paramref name="key"/> (or its inline value), or null when absent.</summary>
    internal static string? Extract(string yaml, string key)
    {
        string[] lines = Split(yaml, out _);
        int start = Find(lines, key);
        if (start < 0) return null;
        string inline = lines[start][(key.Length + 1)..].Trim();
        if (inline.Length > 0) return inline + "\n";
        int end = BlockEnd(lines, start);
        return string.Concat(lines[(start + 1)..end].Select(l => (l.StartsWith("  ") ? l[2..] : l.TrimStart()) + "\n"));
    }

    /// <summary>Replaces (or appends) <paramref name="key"/> with <paramref name="blockYaml"/>; null removes the key.</summary>
    internal static string Replace(string yaml, string key, string? blockYaml)
    {
        string[] lines = Split(yaml, out string nl);
        string block = blockYaml is null ? "" : key + ":" + nl + string.Concat(blockYaml.Replace("\r\n", "\n").TrimEnd('\n').Split('\n')
            .Select(l => (l.Length == 0 ? "" : "  " + l) + nl));
        int start = Find(lines, key);
        if (start < 0) return blockYaml is null ? yaml : (yaml.Length == 0 || yaml.EndsWith('\n') ? yaml : yaml + nl) + block;
        int end = lines[start].Trim() == key + ":" ? BlockEnd(lines, start) : start + 1;
        string before = string.Concat(lines[..start].Select(l => l + nl));
        string after = string.Concat(lines[end..].Select(l => l + nl));
        return before + block + after;
    }

    private static int Find(string[] lines, string key) =>
        Array.FindIndex(lines, l => l.StartsWith(key + ":", StringComparison.Ordinal));

    /// <summary>Index of the first line after <paramref name="start"/> that is non-empty and not indented (or the end).</summary>
    private static int BlockEnd(string[] lines, int start)
    {
        int i = start + 1;
        // a YAML sequence may sit at the same indent as its key ("features:\n- a"); it still belongs to the key
        while (i < lines.Length && (lines[i].Length == 0 || char.IsWhiteSpace(lines[i][0]) || lines[i].StartsWith("- "))) i++;
        while (i > start + 1 && lines[i - 1].Length == 0) i--;   // trailing blank lines belong to what follows
        return i;
    }

    private static string[] Split(string text, out string newline)
    {
        newline = text.Contains("\r\n") ? "\r\n" : "\n";
        string body = text.EndsWith(newline) ? text[..^newline.Length] : text;
        return body.Length == 0 ? [] : body.Split(newline);
    }
}
