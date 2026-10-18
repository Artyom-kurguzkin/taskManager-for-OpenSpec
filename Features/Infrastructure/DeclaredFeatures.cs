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
    private static readonly ISerializer Writer = new SerializerBuilder().Build();

    private static string? Config => OpenSpecStoreApi.OpenSpecRoot is string root ? Path.Combine(root, "config.yaml") : null;

    internal static List<string> Read() =>
        Config is string c && OpenSpecStoreApi.ReadRtmSection(c, Section) is string s && s.Trim().Length > 0 ? Reader.Deserialize<List<string>>(s) ?? [] : [];

    internal static (bool Success, string Message) Add(string path)
    {
        if (Config is not string c) return (false, "No repository is open");
        List<string> list = Read();
        if (!list.Contains(path)) list.Add(path);
        return Write(c, list);
    }

    internal static (bool Success, string Message) Remove(string path)
    {
        if (Config is not string c) return (false, "No repository is open");
        List<string> list = Read();
        return list.Remove(path) ? Write(c, list) : (true, "");
    }

    private static (bool Success, string Message) Write(string config, List<string> list) =>
        OpenSpecStoreApi.WriteRtmSection(config, Section, Writer.Serialize(list).Replace("\r\n", "\n"));
}
