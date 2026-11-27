using System.Globalization;

namespace TaskManager.Helpers;

/// <summary>Native title-bar colours ("#rrggbb"): caption background, caption text, window border.</summary>
public sealed record FrameColors(string Caption, string Text, string Border);

/// <summary>One colour theme of the app's own UI. <see cref="Dark"/> is the page (its CSS block sets color-scheme);
/// <see cref="Frame"/> is the native title bar, which may be dark on a light page (Novita).</summary>
public sealed record Theme(string Id, string Name, bool Dark, FrameColors Frame)
{
    /// <summary>The title bar is dark when its caption colour is (relative luminance under 0.5): the window's dark-title-bar
    /// attribute and the inverted icon follow the caption, not the page.</summary>
    public bool DarkCaption => Luminance(Frame.Caption) < 0.5;

    private static double Luminance(string hex)
    {
        uint rgb = uint.Parse(hex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        static double Lin(uint c) { double s = c / 255.0; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Lin((rgb >> 16) & 0xFF) + 0.7152 * Lin((rgb >> 8) & 0xFF) + 0.0722 * Lin(rgb & 0xFF);
    }
}

/// <summary>The one list of themes in code. The Theme setting holds one of these ids or <see cref="System"/>; the page's
/// html[data-theme] always holds a concrete id (wwwroot/js/theme.js keeps the same id list for the first paint).</summary>
public static class Themes
{
    public const string System = "system";

    // Light and dark: the same values as --paper / --ink / --line-soft in wwwroot/css/app.css.
    public static readonly Theme Light = new("light", "Light", false, new("#f7f4f1", "#24211f", "#d9d3cd"));
    public static readonly Theme Dark = new("dark", "Dark", true, new("#161514", "#ece7e1", "#3a3532"));
    public static readonly Theme Novita = new("novita", "Novita", false, new("#411540", "#FFFFFF", "#64415C"));   // light page, plum title bar
    public static readonly Theme AyuMirage = new("ayu-mirage", "Ayu Mirage", true, new("#1F2430", "#CCCAC2", "#171B24"));

    public static readonly IReadOnlyList<Theme> All = [Light, Dark, Novita, AyuMirage];

    /// <summary>The Settings choices: System first, then every theme.</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> Choices =
        [(System, "System"), .. All.Select(t => (t.Id, t.Name))];

    public static Theme? Find(string? id) => All.FirstOrDefault(t => t.Id == id);

    /// <summary>The concrete theme for a setting: system → light or dark from the OS; null or unknown → light.</summary>
    public static Theme Resolve(string? setting, bool systemDark) =>
        setting == System ? (systemDark ? Dark : Light) : Find(setting) ?? Light;

    /// <summary>The setting as Settings shows it: an unknown saved value shows (and applies) as light.</summary>
    public static string Normalize(string? setting) => setting == System || Find(setting) is not null ? setting! : Light.Id;
}
