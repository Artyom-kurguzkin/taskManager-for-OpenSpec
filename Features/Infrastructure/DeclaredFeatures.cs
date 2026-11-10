using OpenSpecStore;
using YamlDotNet.Serialization;

namespace Features.Infrastructure;

/// <summary>
/// Features added from the board before any spec exists. OpenSpec only accepts a capability that has requirements
/// (it creates one when a change that specs it is archived), so features added from the board are listed in
/// openspec/config.yaml under <c>x-rtm.features</c> instead of as empty spec files.
/// </summary>
internal static class DeclaredFeatures
{
    private const string Section = "features";
    private static readonly IDeserializer Reader = new DeserializerBuilder().Build();
    private static readonly ISerializer Writer = OpenSpecStoreApi.YamlWriter(model: false, flowLists: false);   // a name like "yes" or "123" is written quoted, so it stays text

    private static string? Config => OpenSpecStoreApi.OpenSpecRoot is string root ? Path.Combine(root, "config.yaml") : null;

    /// <summary>The declared features; none when the list cannot be read (a hand edit that is not valid YAML must not take the app down).</summary>
    internal static List<string> Read() => TryRead() ?? [];

    private static List<string>? TryRead()
    {
        try { return Config is string c && OpenSpecStoreApi.ReadRtmSection(c, Section) is string s && s.Trim().Length > 0 ? Reader.Deserialize<List<string>>(s) ?? [] : []; }
        catch (Exception e) when (e is YamlDotNet.Core.YamlException or FormatException or InvalidCastException or InvalidOperationException) { return null; }
    }

    private const string Unreadable = "x-rtm.features in openspec/config.yaml is not valid YAML; fix it first so the features listed there are not lost";

    internal static (bool Success, string Message) Add(string path)
    {
        if (Config is not string c) return (false, "No repository is open");
        if (TryRead() is not { } list) return (false, Unreadable);
        if (!list.Contains(path)) list.Add(path);
        return Write(c, list);
    }

    internal static (bool Success, string Message) Remove(string path)
    {
        if (Config is not string c) return (false, "No repository is open");
        if (TryRead() is not { } list) return (false, Unreadable);
        return list.Remove(path) ? Write(c, list) : (true, "");
    }

    internal static (bool Success, string Message) Rename(string from, string to)
    {
        if (Config is not string c) return (false, "No repository is open");
        if (TryRead() is not { } list) return (false, Unreadable);
        int i = list.IndexOf(from);
        if (i < 0) return (true, "");
        list[i] = to;
        return Write(c, list);
    }

    private static (bool Success, string Message) Write(string config, List<string> list) =>
        OpenSpecStoreApi.WriteRtmSection(config, Section, Writer.Serialize(list).Replace("\r\n", "\n"));
}
