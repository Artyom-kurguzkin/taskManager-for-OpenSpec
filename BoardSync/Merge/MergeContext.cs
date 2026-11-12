using BoardSync.PublicDTOs;

namespace BoardSync.Merge;

/// <summary>What a merge needs besides the three texts: which side wins a true clash, and where the clashes are collected.</summary>
internal sealed class MergeContext(bool remoteIsNewer)
{
    internal bool RemoteIsNewer { get; } = remoteIsNewer;
    internal List<MergeConflict> Conflicts { get; } = [];

    internal T Newer<T>(T local, T remote) => RemoteIsNewer ? remote : local;

    internal void Report(string what, string kept, string dropped) => Conflicts.Add(new MergeConflict(what, kept, dropped));
}

/// <summary>Text as lines: line endings never count, and a final line ending is remembered apart.</summary>
internal static class MergeText
{
    internal static string Normalize(string text) => text.Replace("\r\n", "\n");

    /// <summary>The lines of text that is already normalized; "a\nb\n" and "a\nb" both give [a, b].</summary>
    internal static List<string> Lines(string normalized)
    {
        if (normalized.Length == 0) return [];
        string body = normalized.EndsWith('\n') ? normalized[..^1] : normalized;
        return [.. body.Split('\n')];
    }

    internal static string Join(IReadOnlyList<string> lines, string newline, bool finalNewline) =>
        lines.Count == 0 ? "" : string.Join(newline, lines) + (finalNewline ? newline : "");
}
