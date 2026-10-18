namespace OpenSpecStore;

internal static class Queries
{
    /// <summary>Reads a file (empty when missing) and remembers what was seen, so a later write can detect outside edits.</summary>
    internal static string ReadText(string path)
    {
        string full = Path.GetFullPath(path);
        string? text = File.Exists(full) ? File.ReadAllText(full) : null;
        OpenSpecStoreModule.LastSeen[full] = text;
        return text ?? "";
    }

    internal static string? ReadRtmBlock(string yamlPath) => RtmBlock.Extract(ReadText(yamlPath));

    internal static string? ReadRtmSection(string yamlPath, string key) =>
        RtmBlock.Extract(ReadText(yamlPath)) is string block ? RtmBlock.Extract(block, key) : null;
}
