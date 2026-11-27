using TaskManager.AppState;
using TaskManager.Helpers;

namespace TaskManager.Tests;

public sealed class ThemingTheWindowFrameTests
{
    [Fact(DisplayName = "In the light theme the title bar takes the page colour, with ink-coloured text")]
    public void Light() =>
        Assert.Equal(new FrameColors("#f7f4f1", "#24211f", "#d9d3cd"), Themes.Light.Frame);

    [Fact(DisplayName = "In the dark theme the title bar turns warm dark grey with off-white text")]
    public void Dark() =>
        Assert.Equal(new FrameColors("#161514", "#ece7e1", "#3a3532"), Themes.Dark.Frame);

    [Fact(DisplayName = "Colours are handed to Windows as COLORREF (0x00BBGGRR)")]
    public void ColorRef() => Assert.Equal(0x00F1F4F7u, WindowFrame.ToColorRef("#f7f4f1"));

    [Fact(DisplayName = "The dark theme uses the inverted icon (light signature on dark)")]
    public void Icons()
    {
        Assert.Equal("icon.ico", WindowFrame.IconFor(Themes.Light));
        Assert.Equal("icon-dark.ico", WindowFrame.IconFor(Themes.Dark));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot", "icon-dark.ico")));
    }

    [Fact(DisplayName = "Applying a theme swaps the window icon to match")]
    public void SwapsIcon()
    {
        string? icon = null;
        WindowFrame frame = new(() => IntPtr.Zero, path => icon = path);

        frame.Apply(Themes.Dark);
        Assert.EndsWith(Path.Combine("wwwroot", "icon-dark.ico"), icon);

        frame.Apply(Themes.Light);
        Assert.EndsWith(Path.Combine("wwwroot", "icon.ico"), icon);
    }
}
