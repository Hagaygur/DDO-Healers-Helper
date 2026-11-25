using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VoK.HealersHelper;

internal static class NativeHud
{
    internal const int Layered = 0x80000;
    internal const int Transparent = 0x20;
    internal const int NoActivate = 0x08000000;
    internal const int ToolWindow = 0x80;
    internal const int GwlExStyle = -20;

    internal const int WmNcHitTest = 0x0084;
    internal const int WmMouseActivate = 0x0021;
    internal const int HtCaption = 2;
    internal const int HtTransparent = -1;
    internal const int MaNoActivate = 3;

    internal const uint WdaNone = 0u;
    internal const uint WdaExcludeFromCapture = 0x11u;

    internal const uint UlwAlpha = 2u;
    internal const byte AcSrcAlpha = 1;

    internal static readonly IntPtr HwndTopMost = new(-1);
    internal const uint SwpTopMostFlags = 0x0001u | 0x0002u | 0x0010u; // SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;

        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Size
    {
        public int X;
        public int Y;

        public Size(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct Blend
    {
        public byte Operation;
        public byte Flags;
        public byte Alpha;
        public byte Format;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort Bits;
        public uint Compression;
        public uint ImageSize;
        public int XPixels;
        public int YPixels;
        public uint Used;
        public uint Important;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateLayeredWindow(
        IntPtr window,
        IntPtr destinationDc,
        ref Point destination,
        ref Size size,
        IntPtr sourceDc,
        ref Point source,
        uint key,
        ref Blend blend,
        uint flags);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr SelectObject(IntPtr dc, IntPtr item);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr item);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern IntPtr CreateDIBSection(
        IntPtr dc,
        ref BitmapHeader header,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);
}

internal static class HudHostHelper
{
    public static Rectangle GetTargetScreenBounds(IntPtr gameWindow)
    {
        if (gameWindow != IntPtr.Zero)
        {
            return Screen.FromHandle(gameWindow).Bounds;
        }

        if (Screen.PrimaryScreen is { } primary)
        {
            return primary.Bounds;
        }

        var screens = Screen.AllScreens;
        return screens.Length > 0 ? screens[0].Bounds : new Rectangle(0, 0, 1920, 1080);
    }

    public static string ApplyCaptureExclusion(IntPtr handle, bool exclude)
    {
        uint targetAffinity = exclude ? NativeHud.WdaExcludeFromCapture : NativeHud.WdaNone;
        bool set = NativeHud.SetWindowDisplayAffinity(handle, targetAffinity);
        int error = Marshal.GetLastWin32Error();
        bool verified = set && NativeHud.GetWindowDisplayAffinity(handle, out uint actual) && actual == targetAffinity;
        return exclude
            ? (verified ? "Capture exclusion: ON" : $"Capture exclusion FAILED (Windows error {error})")
            : (verified ? "Capture exclusion: OFF" : $"Capture exclusion reset FAILED (Windows error {error})");
    }

    public static void SetClickThrough(IntPtr handle, bool clickThrough)
    {
        long style = NativeHud.GetWindowLongPtr(handle, NativeHud.GwlExStyle).ToInt64();
        style = clickThrough ? (style | NativeHud.Transparent) : (style & ~NativeHud.Transparent);
        NativeHud.SetWindowLongPtr(handle, NativeHud.GwlExStyle, new IntPtr(style));
    }

    public static bool ForegroundAllowed(IntPtr gameWindow, uint gamePid, ref IntPtr cachedForeground, ref bool cachedAllowed)
    {
        if (gameWindow == IntPtr.Zero)
        {
            return true;
        }

        var current = NativeHud.GetForegroundWindow();
        if (current == cachedForeground)
        {
            return cachedAllowed;
        }

        cachedForeground = current;
        NativeHud.GetWindowThreadProcessId(current, out uint pid);
        cachedAllowed = pid != 0 && (pid == gamePid || pid == Environment.ProcessId);
        if (!cachedAllowed && pid != 0)
        {
            try
            {
                using var process = Process.GetProcessById(checked((int)pid));
                cachedAllowed = process.ProcessName.Replace(" ", "", StringComparison.Ordinal)
                    .Equals("LosslessScaling", StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { }
            catch (Win32Exception) { }
        }

        return cachedAllowed;
    }
}

// Reusable top-down 32bpp premultiplied-alpha DIB surface for zero per-frame native allocations.
internal sealed class HudSurface : IDisposable
{
    private IntPtr _dc;
    private IntPtr _bitmap;
    private IntPtr _original;
    private Bitmap? _image;

    internal Graphics? Graphics { get; private set; }
    internal Bitmap? Image => _image;
    internal System.Drawing.Size Size { get; private set; }

    public HudSurface()
    {
        _dc = NativeHud.CreateCompatibleDC(IntPtr.Zero);
        if (_dc == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    internal void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (Size.Width == width && Size.Height == height)
        {
            return;
        }

        ClearBitmap();
        var header = new NativeHud.BitmapHeader
        {
            Size = 40,
            Width = width,
            Height = -height,
            Planes = 1,
            Bits = 32
        };
        _bitmap = NativeHud.CreateDIBSection(_dc, ref header, 0, out var bits, IntPtr.Zero, 0);
        if (_bitmap == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _original = NativeHud.SelectObject(_dc, _bitmap);
        _image = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, bits);
        Graphics = System.Drawing.Graphics.FromImage(_image);
        Size = new System.Drawing.Size(width, height);
    }

    internal void Present(IntPtr window, System.Drawing.Point location)
    {
        if (window == IntPtr.Zero || Size.IsEmpty)
        {
            return;
        }

        var destination = new NativeHud.Point(location.X, location.Y);
        var source = new NativeHud.Point(0, 0);
        var size = new NativeHud.Size(Size.Width, Size.Height);
        var blend = new NativeHud.Blend { Operation = 0, Flags = 0, Alpha = 255, Format = NativeHud.AcSrcAlpha };
        if (!NativeHud.UpdateLayeredWindow(
                window,
                IntPtr.Zero,
                ref destination,
                ref size,
                _dc,
                ref source,
                0,
                ref blend,
                NativeHud.UlwAlpha))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private void ClearBitmap()
    {
        Graphics?.Dispose();
        Graphics = null;
        _image?.Dispose();
        _image = null;
        if (_bitmap != IntPtr.Zero)
        {
            NativeHud.SelectObject(_dc, _original);
            NativeHud.DeleteObject(_bitmap);
            _bitmap = IntPtr.Zero;
            _original = IntPtr.Zero;
        }

        Size = System.Drawing.Size.Empty;
    }

    public void Dispose()
    {
        ClearBitmap();
        if (_dc != IntPtr.Zero)
        {
            NativeHud.DeleteDC(_dc);
            _dc = IntPtr.Zero;
        }
    }
}
