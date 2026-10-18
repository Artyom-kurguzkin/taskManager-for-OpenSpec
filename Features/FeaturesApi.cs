using Features.PublicDTOs;

namespace Features;

/// <summary>
/// The only public type in this assembly. Capabilities under openspec/specs are features; the first folder
/// of a two-level path is the feature group. Reads straight from disk on every call.
/// </summary>
public static class FeaturesApi
{
    public const string Ungrouped = "Ungrouped";

    public static IReadOnlyList<Feature> GetAll() => Queries.GetAll();

    /// <summary>Adds a feature under a side (Functional, Non-Functional or Ungrouped), listed in config.yaml until OpenSpec creates its spec.</summary>
    public static (bool Success, string Message, Feature? Created) CreateFeature(string group, string name) => Commands.CreateFeature(group, name);

    /// <summary>Removes a custom feature (added from the board) from config.yaml. Features with a spec are refused (only OpenSpec removes those).</summary>
    public static (bool Success, string Message) DeleteFeature(string path) => Commands.DeleteFeature(path);
}
