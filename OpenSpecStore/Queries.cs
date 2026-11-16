using OpenSpecStore.PublicDTOs;

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

    /// <summary>The one place the project's config file is named: <c>openspec/config.yaml</c> of the repository.</summary>
    internal static string ConfigFile(string repoRoot) => Path.Combine(repoRoot, "openspec", "config.yaml");

    internal static string? ConfigFileOf(string? repoRoot) => (repoRoot ?? OpenSpecStoreModule.RepoRoot) is string root ? ConfigFile(root) : null;

    private static readonly YamlDotNet.Serialization.IDeserializer ConfigReader = new YamlDotNet.Serialization.DeserializerBuilder()
        .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance).IgnoreUnmatchedProperties().Build();

    /// <summary>
    /// The one rule for every section of config.yaml: whatever is wrong with it, the call never throws. A section that is missing or empty gives no value and no problem;
    /// one that cannot be read (not valid YAML, not the shape the caller asked for) gives no value and a problem that names it, so the caller uses its default and can say why.
    /// A file that cannot be opened right now (another process holds it) is <see cref="ConfigSection{T}.Unavailable"/>.
    /// </summary>
    internal static ConfigSection<T> ReadConfigSection<T>(string key, string? repoRoot)
    {
        if (ConfigFileOf(repoRoot) is not string file || !File.Exists(file)) return new(default);   // a file that is not there is not "seen as missing": a writer that creates it later is no conflict
        string text;
        try { text = ReadText(file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new(default, $"openspec/config.yaml could not be opened ({e.Message.TrimEnd('.')})", Unavailable: true); }
        if (RtmBlock.Extract(text) is not string block || RtmBlock.Extract(block, key) is not string yaml || yaml.Trim().Length == 0) return new(default);
        try { return new(ConfigReader.Deserialize<T>(yaml.Replace("\r\n", "\n").Replace('\r', '\n'))); }
        catch (Exception e) when (e is YamlDotNet.Core.YamlException or FormatException or InvalidCastException or InvalidOperationException)
        {
            string reason = (e.InnerException ?? e).Message;
            reason = System.Text.RegularExpressions.Regex.Replace(reason, @"^\(Line:[^)]*\)\s*-\s*\(Line:[^)]*\):\s*", "").TrimEnd('.');
            return new(default, $"x-rtm.{key} in openspec/config.yaml could not be read ({(reason.Length > 1 && char.IsUpper(reason[0]) && char.IsLower(reason[1]) ? char.ToLowerInvariant(reason[0]) + reason[1..] : reason)}); the default is used until the file is fixed");
        }
    }
}
