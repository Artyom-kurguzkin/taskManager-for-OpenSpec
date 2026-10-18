using MudBlazor;

namespace TaskManager.Components.Layout;

/// <summary>MudBlazor palette built from the design tokens in design/mockup.html (light and dark are designed separately).</summary>
public static class AppTheme
{
    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#24211f", Secondary = "#f3da6c", Tertiary = "#d3e39c",
            Background = "#f7f4f1", Surface = "#ffffff", TextPrimary = "#24211f", TextSecondary = "#6f6a66",
            Success = "#2f7d32", Warning = "#c4683f", Error = "#b3402a", Info = "#3c6ea8",
            LinesDefault = "#d9d3cd", Divider = "#d9d3cd", AppbarBackground = "#f7f4f1", AppbarText = "#24211f",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#ece7e1", Secondary = "#d4bd5f", Tertiary = "#b2c483",
            Background = "#161514", Surface = "#221f1d", TextPrimary = "#ece7e1", TextSecondary = "#a9a29b",
            Success = "#9fd393", Warning = "#d89479", Error = "#e08a72", Info = "#9db7d4",
            LinesDefault = "#3a3532", Divider = "#3a3532", AppbarBackground = "#161514", AppbarText = "#ece7e1",
        },
        Typography = new Typography { Default = new DefaultTypography { FontFamily = ["Plus Jakarta Sans", "Segoe UI", "system-ui", "sans-serif"] } },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "14px" },
    };
}
