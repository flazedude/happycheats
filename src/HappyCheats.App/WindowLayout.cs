using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace HappyCheats.App;

/// <summary>Keeps normal windows usable on small, scaled displays without scaling their content.</summary>
internal static class WindowLayout
{
    public static void Attach(Window window)
    {
        var preferredMinWidth = window.MinWidth;
        var preferredMinHeight = window.MinHeight;
        HwndSource? source = null;
        var lastMonitor = IntPtr.Zero;
        var queued = false;
        var closed = false;

        void Fit()
        {
            queued = false;
            if (closed || !window.IsLoaded) return;
            var handle = new WindowInteropHelper(window).Handle;
            var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) return;
            lastMonitor = monitor;

            var dpi = VisualTreeHelper.GetDpi(window);
            var availableWidth = (info.Work.Right - info.Work.Left) / dpi.DpiScaleX;
            var availableHeight = (info.Work.Bottom - info.Work.Top) / dpi.DpiScaleY;
            if (availableWidth <= 0 || availableHeight <= 0) return;

            // The work area is in physical pixels; WPF sizing uses device independent pixels.
            // Lower minimums too, otherwise the original minimum can defeat the size clamp.
            window.MinWidth = Math.Min(preferredMinWidth, availableWidth);
            window.MinHeight = Math.Min(preferredMinHeight, availableHeight);
            if (window.WindowState != WindowState.Normal) return;
            if (window.ActualWidth > availableWidth) window.Width = availableWidth;
            if (window.ActualHeight > availableHeight) window.Height = availableHeight;

            // Use native coordinates for positioning so mixed-DPI monitor origins remain correct.
            if (!GetWindowRect(handle, out var bounds)) return;
            var left = Math.Clamp(bounds.Left, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - bounds.Width));
            var top = Math.Clamp(bounds.Top, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - bounds.Height));
            if (left != bounds.Left || top != bounds.Top)
                SetWindowPos(handle, IntPtr.Zero, left, top, 0, 0, NoSize | NoZOrder | NoActivate);
        }

        void QueueFit()
        {
            if (queued || closed) return;
            queued = true;
            // Let WPF apply a DPI change before reading its device scale and arranging the content.
            window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Fit));
        }

        IntPtr WindowMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message is DisplayChanged or SettingsChanged) QueueFit();
            return IntPtr.Zero;
        }

        window.SourceInitialized += (_, _) =>
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            source?.AddHook(WindowMessage);
        };
        window.Loaded += (_, _) => Fit();
        window.DpiChanged += (_, _) => QueueFit();
        window.LocationChanged += (_, _) =>
        {
            if (!window.IsLoaded || closed) return;
            var monitor = MonitorFromWindow(new WindowInteropHelper(window).Handle, MonitorDefaultToNearest);
            if (monitor != lastMonitor) QueueFit();
        };
        window.Closed += (_, _) => { closed = true; source?.RemoveHook(WindowMessage); };
    }

    private const uint MonitorDefaultToNearest = 2;
    private const uint NoSize = 0x0001, NoZOrder = 0x0004, NoActivate = 0x0010;
    private const int DisplayChanged = 0x007E, SettingsChanged = 0x001A;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
