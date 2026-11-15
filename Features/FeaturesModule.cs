namespace Features;

/// <summary>The module's state: the parsed features per repository and the counter tests read.</summary>
internal sealed class FeaturesState
{
    public readonly Dictionary<string, (long Version, IReadOnlyList<PublicDTOs.Feature> Features)> Cache = new(StringComparer.OrdinalIgnoreCase);   // per repository
    public int Loads;
}

/// <summary>Process-wide state: the parsed features, valid while OpenSpecStore's content version is unchanged. The app has one such state; a test runs against its own (<see cref="Isolated"/>).</summary>
internal static class FeaturesModule
{
    private static readonly FeaturesState Shared = new();

    /// <summary>A test seam: the state of the code running from where it was set (one per test); null = the process-wide one, which is all the app ever uses.</summary>
    internal static readonly AsyncLocal<FeaturesState?> Isolated = new();

    private static FeaturesState S => Isolated.Value ?? Shared;

    internal static Dictionary<string, (long Version, IReadOnlyList<PublicDTOs.Feature> Features)> Cache => S.Cache;   // per repository
    internal static int Loads { get => S.Loads; set => S.Loads = value; }
}
