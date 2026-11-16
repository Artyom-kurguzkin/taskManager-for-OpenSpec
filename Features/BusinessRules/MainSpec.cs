using Features.PublicDTOs;
using OpenSpecStore;

namespace Features;

/// <summary>Reading a capability's main spec (openspec/specs/&lt;capability&gt;/spec.md) and naming capabilities.</summary>
internal static class MainSpec
{
    /// <summary>Why a text cannot be a capability path (null when it can): empty, rooted, or climbing out with "..".</summary>
    internal static string? CheckCapability(string? capability)
    {
        string c = (capability ?? "").Replace('\\', '/').Trim('/');
        return c.Length == 0 || Path.IsPathRooted(c) || c.Split('/').Contains("..")
            ? $"\"{capability}\" is not a capability: use a path under openspec/specs, like billing or functional/banking (see board_features)."
            : null;
    }

    /// <summary>The feature a path names (separators and case ignored) and the requirements of its main spec; <paramref name="requirement"/> adds one requirement's text.</summary>
    internal static SpecRead Read(string feature, string? requirement)
    {
        IReadOnlyList<Feature> all = Queries.GetAll();
        string path = feature.Replace('\\', '/').Trim('/', ' ');
        Feature? found = all.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
        if (found is null) return new(false, $"There is no feature \"{feature}\". The features are: {string.Join(", ", all.Select(f => f.Path))}.", null, [], null);
        string file = Path.Combine(OpenSpecStoreApi.OpenSpecRoot!, "specs", found.Path.Replace('/', Path.DirectorySeparatorChar), "spec.md");
        if (!File.Exists(file)) return new(false, $"The feature {found.Path} has no spec yet (it was added from the board): there are no requirements to read.", found, [], null);
        List<(string Name, string Text)> requirements = Requirements(OpenSpecStoreApi.ReadText(file));
        List<string> names = [.. requirements.Select(r => r.Name)];
        string? text = null;
        if (!string.IsNullOrWhiteSpace(requirement))
        {
            string want = requirement.Trim();
            if (want.StartsWith("Requirement:", StringComparison.OrdinalIgnoreCase)) want = want["Requirement:".Length..].Trim();
            text = requirements.FirstOrDefault(r => string.Equals(r.Name, want, StringComparison.OrdinalIgnoreCase)).Text;
            if (text is null) return new(false, $"{found.Path} has no requirement \"{requirement}\". Its requirements are: {string.Join("; ", names)}.", found, names, null);
        }
        return new(true, "", found, names, text);
    }

    /// <summary>The requirements of a main spec: each name with its text, from its "### Requirement:" line up to the next requirement or "## " heading.</summary>
    internal static List<(string Name, string Text)> Requirements(string spec)
    {
        List<(string Name, string Text)> found = [];
        string? name = null;
        List<string> lines = [];
        void Close() { if (name is not null) found.Add((name, string.Join("\n", lines).Trim())); name = null; lines = []; }
        foreach (string line in spec.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("### Requirement:", StringComparison.Ordinal)) { Close(); name = line["### Requirement:".Length..].Trim(); }
            else if (line.StartsWith("## ", StringComparison.Ordinal)) { Close(); continue; }
            if (name is not null) lines.Add(line);
        }
        Close();
        return found;
    }
}
