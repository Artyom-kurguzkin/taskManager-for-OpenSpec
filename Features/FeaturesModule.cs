namespace Features;

/// <summary>The module keeps no cache: only the host's cached read, handed in once (see FeaturesApi.UseReader).</summary>
internal static class FeaturesModule
{
    internal static Func<IReadOnlyList<PublicDTOs.Feature>>? Reader;
}
