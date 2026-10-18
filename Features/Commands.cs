using Features.Infrastructure;
using Features.PublicDTOs;
using OpenSpecStore;

namespace Features;

internal static class Commands
{
    internal static (bool Success, string Message, Feature? Created) CreateSubgroup(string group, string name)
    {
        if (OpenSpecStoreApi.OpenSpecRoot is null) return (false, "No repository is open", null);
        if (string.IsNullOrWhiteSpace(name)) return (false, "Give the sub-group a name", null);

        // Reuse the existing folder for this group name; a new group gets a kebab-case folder.
        IReadOnlyList<Feature> all = Queries.GetAll();
        string groupFolder = all.FirstOrDefault(f => f.Group == group && f.Path.Contains('/'))?.Path.Split('/')[0] ?? Naming.Slug(group);
        string path = group == FeaturesApi.Ungrouped ? Naming.Slug(name) : $"{groupFolder}/{Naming.Slug(name)}";
        if (all.Any(f => f.Path == path)) return (false, $"{name.Trim()} already exists in {group}", null);

        (bool ok, string message) = DeclaredFeatures.Add(path);
        return ok ? (true, "", Queries.ToFeature(path)) : (false, message, null);
    }
}
