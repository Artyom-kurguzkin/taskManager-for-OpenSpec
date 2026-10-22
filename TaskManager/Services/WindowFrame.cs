using System.Globalization;
using System.Runtime.InteropServices;

namespace TaskManager.Services;

public sealed record FrameColors(string Caption, string Text, string Border);

/// <summary>
/// Paints the native title bar in the theme's colours (Windows 11 DWM attributes), so the frame blends with the
/// page the way VS Code's does, while keeping native dragging, snap layouts and min/max/close, and swaps the window icon
/// to the theme's (the exe file keeps the light one: Windows has no per-theme exe icons).
/// Caption colours are a no-op on older Windows, macOS and Linux.
/// </summary>
public sealed partial class WindowFrame(Func<IntPtr> windowHandle, Action<string>? setIcon = null)   // setIcon: tests capture the choice
{
    /// <summary>File in wwwroot: the signature in ink on paper, or inverted for the dark theme.</summary>
    public static string IconFor(bool dark) => dark ? "icon-dark.ico" : "icon.ico";

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
        string icon = Path.Combine(AppContext.BaseDirectory, "wwwroot", IconFor(dark));
        if (setIcon is not null) setIcon(icon);
        else if (OperatingSystem.IsWindows() && windowHandle() is var h && h != IntPtr.Zero)
        {
            SetIcon(h, icon);
            // The bar keeps dragging and min/max/close but draws no title text; taskbar and Alt-Tab still show it.
            WtaOptions bare = new() { Flags = NoDrawCaption | NoDrawIcon, Mask = NoDrawCaption | NoDrawIcon };
            SetWindowThemeAttribute(h, NonClient, ref bare, (uint)Marshal.SizeOf<WtaOptions>());
        }
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;   // caption colours need Windows 11
        IntPtr hwnd = windowHandle();
        if (hwnd == IntPtr.Zero) return;
        FrameColors c = ColorsFor(dark);
        Set(hwnd, DarkMode, dark ? 1u : 0u);
        Set(hwnd, CaptionColor, ToColorRef(c.Caption));
        Set(hwnd, TextColor, ToColorRef(c.Text));
        Set(hwnd, BorderColor, ToColorRef(c.Border));
    }

    // Set here rather than with Photino's SetIconFile, which only works before the window opens and sets a small icon too.
    private static void SetIcon(IntPtr hwnd, string path)
    {
        const uint ImageIcon = 1, LoadFromFile = 0x10, WmSetIcon = 0x80;
        IntPtr big = LoadImageW(IntPtr.Zero, path, ImageIcon, 32, 32, LoadFromFile);
        if (big != IntPtr.Zero) SendMessageW(hwnd, WmSetIcon, 1, big);   // ICON_BIG: taskbar, Alt-Tab
        // ICON_SMALL (title bar) is a fully transparent icon: with none set, Windows derives one from the big icon
        // when the window first shows, so clearing it doesn't stick.
        SendMessageW(hwnd, WmSetIcon, 0, Blank.Value);
    }

    private static readonly Lazy<IntPtr> Blank = new(() => CreateIcon(IntPtr.Zero, 16, 16, 1, 1,
        [.. Enumerable.Repeat((byte)0xFF, 32)], new byte[32]));   // AND mask all set + XOR all clear = see-through

    [LibraryImport("user32.dll")]
    private static partial IntPtr CreateIcon(IntPtr instance, int width, int height, byte planes, byte bitsPerPixel, byte[] andBits, byte[] xorBits);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr LoadImageW(IntPtr instance, string name, uint type, int cx, int cy, uint load);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SendMessageW(IntPtr hwnd, uint msg, nint wParam, IntPtr lParam);

    private const int NonClient = 1;                                // WTA_NONCLIENT
    private const uint NoDrawCaption = 0x1, NoDrawIcon = 0x2;      // WTNCA_*

    [StructLayout(LayoutKind.Sequential)]
    private struct WtaOptions { public uint Flags; public uint Mask; }

    [LibraryImport("uxtheme.dll")]
    private static partial int SetWindowThemeAttribute(IntPtr hwnd, int attribute, ref WtaOptions options, uint size);

    private const int DarkMode = 20, BorderColor = 34, CaptionColor = 35, TextColor = 36;   // DWMWINDOWATTRIBUTE

    private static void Set(IntPtr hwnd, int attribute, uint value) => DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(uint));

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);
}
