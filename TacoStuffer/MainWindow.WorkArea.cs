using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace TacoStuffer;

public partial class MainWindow
{
    private void InitializeWorkAreaBounds()
    {
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WorkAreaHook);
    }
    private nint WorkAreaHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != 0x0024) return 0; // WM_GETMINMAXINFO uses physical monitor pixels, including mixed-DPI monitors.
        nint monitor = MonitorFromWindow(hwnd,2);
        var info = new MonitorBounds { Size = Marshal.SizeOf<MonitorBounds>() };
        if (monitor == 0 || !GetMonitorInfo(monitor,ref info)) return 0;
        var size = Marshal.PtrToStructure<MaximizeBounds>(lParam);
        size.MaxPosition = new NativePoint { X = info.Work.Left - info.Monitor.Left, Y = info.Work.Top - info.Monitor.Top };
        size.MaxSize = new NativePoint { X = info.Work.Right - info.Work.Left, Y = info.Work.Bottom - info.Work.Top };
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        size.MinTrackSize.X = Math.Max(size.MinTrackSize.X,(int)Math.Ceiling(MinWidth*dpi.DpiScaleX));
        size.MinTrackSize.Y = Math.Max(size.MinTrackSize.Y,(int)Math.Ceiling(MinHeight*dpi.DpiScaleY));
        Marshal.StructureToPtr(size,lParam,false); handled = true; return 0;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MaximizeBounds { public NativePoint Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorBounds { public int Size; public NativeRect Monitor,Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window,uint flags);
    [DllImport("user32.dll",EntryPoint="GetMonitorInfoW")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor,ref MonitorBounds bounds);
}
