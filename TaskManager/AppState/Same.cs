using System.Text.Json;

namespace TaskManager.AppState;

/// <summary>"Equal" for what the caches hold: a value read again that says the same as the old one is not a change, so the old instance stays and nothing redraws.</summary>
internal static class Same
{
    /// <summary>Whether the two say the same (compared by their content, lists and all).</summary>
    internal static bool Value<T>(T a, T b) => ReferenceEquals(a, b) || JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);

    /// <summary><paramref name="old"/> when <paramref name="now"/> says the same as it, else <paramref name="now"/>.</summary>
    internal static T Keep<T>(T? old, T now) where T : class => old is not null && Value(old, now) ? old : now;
}
