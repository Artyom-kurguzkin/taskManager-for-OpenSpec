namespace Features;

/// <summary>Folder names ↔ display names. "non-functional" → "Non-Functional" (group), "unbilled-follow-up" → "Unbilled follow up" (feature).</summary>
internal static class Naming
{
    internal static string Group(string folder) => string.Join('-', folder.Split('-').Select(Capitalise));

    internal static string Name(string folder) => Capitalise(folder.Replace('-', ' '));

    internal static string Slug(string display) =>
        string.Join('-', display.Trim().ToLowerInvariant().Split(' ', '_', '/').Where(p => p.Length > 0));

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
