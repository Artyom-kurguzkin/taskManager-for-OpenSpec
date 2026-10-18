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

    /// <summary>Creates openspec/specs/&lt;group&gt;/&lt;name&gt;/spec.md with a Purpose placeholder.</summary>
    public static (bool Success, string Message, Feature? Created) CreateSubgroup(string group, string name) => Commands.CreateSubgroup(group, name);
}
