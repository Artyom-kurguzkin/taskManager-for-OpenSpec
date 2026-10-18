using System.Globalization;
using System.Runtime.InteropServices;

namespace TaskManager.Services;

public sealed record FrameColors(string Caption, string Text, string Border);

/// <summary>
/// Paints the native title bar in the theme's colours (Windows 11 DWM attributes), so the frame blends with the
/// page the way VS Code's does, while keeping native dragging, snap layouts and min/max/close.
/// No-op on older Windows, macOS and Linux.
/// </summary>
public sealed partial class WindowFrame(Func<IntPtr> windowHandle)
{
    // Same values as --paper / --ink / --line-soft in wwwroot/css/app.css.
    public static FrameColors ColorsFor(bool dark) => dark
        ? new FrameColors("#161514", "#ece7e1", "#3a3532")
        : new FrameColors("#f7f4f1", "#24211f", "#d9d3cd");

    /// <summary>"#rrggbb" → COLORREF 0x00BBGGRR.</summary>
    public static uint ToColorRef(string hex)
    {
        uint rgb = uint.Parse(hex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);
    }

    public void Apply(bool dark)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;   // caption colours need Windows 11
        IntPtr hwnd = windowHandle();
        if (hwnd == IntPtr.Zero) return;
        FrameColors c = ColorsFor(dark);
        Set(hwnd, DarkMode, dark ? 1u : 0u);
        Set(hwnd, CaptionColor, ToColorRef(c.Caption));
        Set(hwnd, TextColor, ToColorRef(c.Text));
        Set(hwnd, BorderColor, ToColorRef(c.Border));
    }

    private const int DarkMode = 20, BorderColor = 34, CaptionColor = 35, TextColor = 36;   // DWMWINDOWATTRIBUTE

    private static void Set(IntPtr hwnd, int attribute, uint value) => DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(uint));

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);
}
