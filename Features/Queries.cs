using Features.Infrastructure;
using Features.PublicDTOs;
using OpenSpecStore;

namespace Features;

internal static class Queries
{
    // ponytail: re-scans openspec/specs on every call (tens of folders); cache on OpenSpecStoreApi.Changed if it ever shows in a profile.
    /// <summary>Capabilities with a spec.md, plus features listed in config.yaml that have no spec yet.</summary>
    internal static IReadOnlyList<Feature> GetAll()
    {
        if (OpenSpecStoreApi.OpenSpecRoot is not string root) return [];
        string specs = Path.Combine(root, "specs");
        List<string> real = Directory.Exists(specs)
            ? Directory.EnumerateFiles(specs, "spec.md", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(specs, Path.GetDirectoryName(f)!).Replace('\\', '/')).ToList()
            : [];
        return real.Concat(DeclaredFeatures.Read().Except(real))
            .Select(ToFeature)
            .OrderBy(f => f.Group == FeaturesApi.Ungrouped ? "" : f.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static Feature ToFeature(string path)
    {
        string[] parts = path.Split('/');
        string group = parts.Length > 1 ? Naming.Group(parts[0]) : FeaturesApi.Ungrouped;
        return new Feature(path, Naming.Name(parts[^1]), group);
    }
}
