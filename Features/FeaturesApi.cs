using Features.PublicDTOs;

namespace Features;

/// <summary>
/// The only public type in this assembly. Capabilities under openspec/specs are features; the first folder
/// of a two-level path is the feature group. Reads straight from disk on every call, unless the host installed a cache (<see cref="UseReader"/>).
/// </summary>
public static class FeaturesApi
{
    public const string Ungrouped = "Ungrouped";

    /// <summary>The features: from the host's cache when it installed one, else read afresh.</summary>
    public static IReadOnlyList<Feature> GetAll() => Queries.GetAll();

    /// <summary>The features, read from disk now: capabilities with a spec.md, plus the ones listed in config.yaml that have no spec yet. The module keeps nothing; the host's cache decides when to ask.</summary>
    public static IReadOnlyList<Feature> Read() => Queries.Load();

    /// <summary>Hands the module the host's cached read for its own code and for <see cref="GetAll"/>; null = read afresh on every call.</summary>
    public static void UseReader(Func<IReadOnlyList<Feature>>? reader) => FeaturesModule.Reader = reader;

    /// <summary>Why a text cannot be used as a capability path (rooted, empty or climbing with ".."), or null when it can.</summary>
    public static string? CheckCapability(string? capability) => MainSpec.CheckCapability(capability);

    /// <summary>
    /// Reads the main spec of a capability: its requirement names, and with <paramref name="requirement"/> that requirement's full text with its scenarios.
    /// Unsuccessful, with the reason in Message, for an unknown feature, a feature without a spec yet, or an unknown requirement.
    /// </summary>
    public static SpecRead ReadSpec(string feature, string? requirement = null) => MainSpec.Read(feature, requirement);

    /// <summary>Adds a feature under a side (Functional, Non-Functional or Ungrouped), listed in config.yaml until OpenSpec creates its spec.</summary>
    public static (bool Success, string Message, Feature? Created) CreateFeature(string group, string name) => Commands.CreateFeature(group, name);

    /// <summary>Removes a custom feature (added from the board) from config.yaml. Features with a spec are refused (only OpenSpec removes those).</summary>
    public static (bool Success, string Message) DeleteFeature(string path) => Commands.DeleteFeature(path);

    /// <summary>Renames a feature: moves its spec folder or its config.yaml entry. Work items are updated by WorkItemsApi.RenameFeature.</summary>
    public static (bool Success, string Message, string? NewPath) RenameFeature(string path, string name) => Commands.RenameFeature(path, name);
}
