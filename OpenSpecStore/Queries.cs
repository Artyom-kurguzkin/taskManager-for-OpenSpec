namespace OpenSpecStore;

internal static class Queries
{
    /// <summary>Reads a file (empty when missing) and remembers what was seen, so a later write can detect outside edits.</summary>
    internal static string ReadText(string path)
    {
        string full = Path.GetFullPath(path);
        string? text = ReadShared(full);
        OpenSpecStoreModule.LastSeen[full] = text;
        return text ?? "";
    }

    /// <summary>
    /// The file's text, or null when it is missing. Opened sharing read, write and delete, as the board server, the OpenSpec CLI and the sync
    /// hold these files too; File.ReadAllText (FileShare.Read) throws while any of them has the file open for writing.
    /// </summary>
    internal static string? ReadShared(string full)
    {
        if (!File.Exists(full)) return null;
        using FileStream stream = new(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    internal static string? ReadRtmBlock(string yamlPath) => RtmBlock.Extract(ReadText(yamlPath));

    internal static string? ReadRtmSection(string yamlPath, string key) =>
        RtmBlock.Extract(ReadText(yamlPath)) is string block ? RtmBlock.Extract(block, key) : null;
}
