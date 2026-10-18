namespace Features;

/// <summary>Process-wide state: the parsed features, valid while OpenSpecStore's content version is unchanged.</summary>
internal static class FeaturesModule
{
    internal static (long Version, IReadOnlyList<PublicDTOs.Feature> Features)? Cache;
    internal static int Loads;
}
