using TaskManager.Services;

namespace TaskManager.Tests;

public sealed class ThemingTheWindowFrameTests
{
    [Fact(DisplayName = "In the light theme the title bar takes the page colour, with ink-coloured text")]
    public void Light() =>
        Assert.Equal(new FrameColors("#f7f4f1", "#24211f", "#d9d3cd"), WindowFrame.ColorsFor(dark: false));

    [Fact(DisplayName = "In the dark theme the title bar turns warm dark grey with off-white text")]
    public void Dark() =>
        Assert.Equal(new FrameColors("#161514", "#ece7e1", "#3a3532"), WindowFrame.ColorsFor(dark: true));

    [Fact(DisplayName = "Colours are handed to Windows as COLORREF (0x00BBGGRR)")]
    public void ColorRef() => Assert.Equal(0x00F1F4F7u, WindowFrame.ToColorRef("#f7f4f1"));
}
