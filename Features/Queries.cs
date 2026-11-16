using Features.Infrastructure;
using Features.PublicDTOs;
using OpenSpecStore;

namespace Features;

internal static class Queries
{
    /// <summary>The features: from the host's cache when it installed one (see FeaturesApi.UseReader), else read afresh.</summary>
    internal static IReadOnlyList<Feature> GetAll() => FeaturesModule.Reader?.Invoke() ?? Load();

    /// <summary>Capabilities with a spec.md, plus features listed in config.yaml that have no spec yet. Read from disk, kept nowhere.</summary>
    internal static IReadOnlyList<Feature> Load()
    {
        if (OpenSpecStoreApi.OpenSpecRoot is not string root) return [];
        string specs = Path.Combine(root, "specs");
        List<string> real = Directory.Exists(specs)
            ? Directory.EnumerateFiles(specs, "spec.md", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(specs, Path.GetDirectoryName(f)!).Replace('\\', '/')).ToList()
            : [];
        return real.Select(p => ToFeature(p)).Concat(DeclaredFeatures.Read().Except(real).Select(p => ToFeature(p, custom: true)))
            .OrderBy(f => f.Group == FeaturesApi.Ungrouped ? "" : f.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static Feature ToFeature(string path, bool custom = false)
    {
        string[] parts = path.Split('/');
        string group = parts.Length > 1 ? Naming.Group(parts[0]) : FeaturesApi.Ungrouped;
        return new Feature(path, Naming.Name(parts[^1]), group, custom);
    }
}
