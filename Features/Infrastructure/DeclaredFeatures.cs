using OpenSpecStore;
using OpenSpecStore.PublicDTOs;

namespace Features.Infrastructure;

/// <summary>
/// Features added from the board before any spec exists. OpenSpec only accepts a capability that has requirements
/// (it creates one when a change that specs it is archived), so features added from the board are listed in
/// openspec/config.yaml under <c>x-rtm.features</c> instead of as empty spec files.
/// </summary>
internal static class DeclaredFeatures
{
    private const string Section = "features";

    /// <summary>The declared features; none when the list cannot be read (a hand edit that is not valid YAML must not take the app down).</summary>
    internal static List<string> Read() => TryRead() ?? [];

    private static List<string>? TryRead()
    {
        ConfigSection<List<string>> read = OpenSpecStoreApi.ReadConfigSection<List<string>>(Section);
        return read.Problem is not null ? null : read.Value ?? [];
    }

    private const string Unreadable = "x-rtm.features in openspec/config.yaml is not valid YAML; fix it first so the features listed there are not lost";

    internal static (bool Success, string Message) Add(string path)
    {
        if (OpenSpecStoreApi.RepoRoot is null) return (false, "No repository is open");
        if (TryRead() is not { } list) return (false, Unreadable);
        if (!list.Contains(path)) list.Add(path);
        return Write(list);
    }

    internal static (bool Success, string Message) Remove(string path)
    {
        if (OpenSpecStoreApi.RepoRoot is null) return (false, "No repository is open");
        if (TryRead() is not { } list) return (false, Unreadable);
        return list.Remove(path) ? Write(list) : (true, "");
    }

    internal static (bool Success, string Message) Rename(string from, string to)
    {
        if (OpenSpecStoreApi.RepoRoot is null) return (false, "No repository is open");
        if (TryRead() is not { } list) return (false, Unreadable);
        int i = list.IndexOf(from);
        if (i < 0) return (true, "");
        list[i] = to;
        return Write(list);
    }

    private static (bool Success, string Message) Write(List<string> list) =>
        OpenSpecStoreApi.WriteConfigSection(Section, list);   // a name like "yes" or "123" is written quoted, so it stays text
}
