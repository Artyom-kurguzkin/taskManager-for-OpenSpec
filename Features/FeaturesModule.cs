namespace Features;

/// <summary>Process-wide state: the parsed features, valid while OpenSpecStore's content version is unchanged.</summary>
internal static class FeaturesModule
{
    internal static readonly Dictionary<string, (long Version, IReadOnlyList<PublicDTOs.Feature> Features)> Cache = new(StringComparer.OrdinalIgnoreCase);   // per repository
    internal static int Loads;
}
