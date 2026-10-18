using Features.Infrastructure;
using Features.PublicDTOs;
using OpenSpecStore;

namespace Features;

internal static class Queries
{
    /// <summary>Capabilities with a spec.md, plus features listed in config.yaml that have no spec yet. Cached per content version.</summary>
    internal static IReadOnlyList<Feature> GetAll()
    {
        long version = OpenSpecStoreApi.Version;
        if (FeaturesModule.Cache is { } c && c.Version == version) return c.Features;
        IReadOnlyList<Feature> features = Load();
        FeaturesModule.Cache = (version, features);
        FeaturesModule.Loads++;
        return features;
    }

    private static IReadOnlyList<Feature> Load()
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
