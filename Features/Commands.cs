using Features.Infrastructure;
using Features.PublicDTOs;
using OpenSpecStore;

namespace Features;

internal static class Commands
{
    internal static (bool Success, string Message, Feature? Created) CreateFeature(string group, string name)
    {
        if (OpenSpecStoreApi.OpenSpecRoot is null) return (false, "No repository is open", null);
        if (string.IsNullOrWhiteSpace(name)) return (false, "Give the feature a name", null);

        // Reuse the existing folder for this group name; a new group gets a kebab-case folder.
        IReadOnlyList<Feature> all = Queries.GetAll();
        string groupFolder = all.FirstOrDefault(f => f.Group == group && f.Path.Contains('/'))?.Path.Split('/')[0] ?? Naming.Slug(group);
        string path = group == FeaturesApi.Ungrouped ? Naming.Slug(name) : $"{groupFolder}/{Naming.Slug(name)}";
        if (all.Any(f => f.Path == path)) return (false, $"{name.Trim()} already exists in {group}", null);

        (bool ok, string message) = DeclaredFeatures.Add(path);
        return ok ? (true, "", Queries.ToFeature(path, custom: true)) : (false, message, null);
    }

    internal static (bool Success, string Message) DeleteFeature(string path)
    {
        if (Queries.GetAll().FirstOrDefault(f => f.Path == path) is not Feature feature) return (false, "That feature no longer exists");
        if (!feature.Custom)   // a spec folder: it goes with its spec.md, unless it also holds other features
        {
            string dir = Path.Combine(OpenSpecStoreApi.OpenSpecRoot!, "specs", path.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.EnumerateFiles(dir, "spec.md", SearchOption.AllDirectories).Count() > 1) return (false, $"{feature.Name}'s folder holds other features; delete those first");
            (bool ok, string message) = OpenSpecStoreApi.DeleteFolder(dir);
            if (!ok) return (ok, message);
        }
        return DeclaredFeatures.Remove(path);
    }

    /// <summary>Gives a feature a new name: its folder (last path segment) changes, so does the spec folder or the config.yaml entry. Returns the new path.</summary>
    internal static (bool Success, string Message, string? NewPath) RenameFeature(string path, string name)
    {
        if (Queries.GetAll().FirstOrDefault(f => f.Path == path) is not Feature feature) return (false, "That feature no longer exists", null);
        if (Naming.Slug(name) is not { Length: > 0 } slug) return (false, "Give the feature a name", null);
        string newPath = path.Contains('/') ? $"{path[..path.LastIndexOf('/')]}/{slug}" : slug;
        if (newPath == path) return (true, "", path);
        if (Queries.GetAll().Any(f => f.Path == newPath)) return (false, $"{name.Trim()} already exists", null);
        if (!feature.Custom)
        {
            string specs = Path.Combine(OpenSpecStoreApi.OpenSpecRoot!, "specs");
            string dir = Path.Combine(specs, path.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.EnumerateFiles(dir, "spec.md", SearchOption.AllDirectories).Count() > 1) return (false, $"{feature.Name}'s folder holds other features; rename those instead", null);
            (bool moved, string why) = OpenSpecStoreApi.MoveFolder(dir, Path.Combine(specs, newPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!moved) return (false, why, null);
        }
        (bool ok, string message) = DeclaredFeatures.Rename(path, newPath);   // also when spec-backed: it may still be listed from before its spec existed
        return ok ? (true, "", newPath) : (false, message, null);
    }
}
