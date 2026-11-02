using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TacoStuffer.Services;

public readonly record struct GameClientWindow(int ProcessId, nint Handle)
{
    public static GameClientWindow Find(uint linkedProcess)
    {
        if (linkedProcess > 0 && linkedProcess <= int.MaxValue)
        {
            try
            {
                using var p = Process.GetProcessById((int)linkedProcess);
                if (!p.HasExited && IsGame(p.ProcessName)) return new(p.Id, FindClientHandle(p));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
            {
            }
            return default; // A populated link must belong to this exact client.
        }
        foreach (string name in new[] { "Gw2-64", "Gw2", "GuildWars2" })
        {
            var candidates = Process.GetProcessesByName(name);
            GameClientWindow found = default;
            bool foundRender = false;
            foreach (var p in candidates)
            {
                try
                {
                    if (!p.HasExited && !foundRender)
                    {
                        nint render = FindRenderHandle(p.Id);
                        if (render != 0)
                        {
                            found = new(p.Id, render);
                            foundRender = true;
                        }
                        else if (found.ProcessId == 0) found = new(p.Id, p.MainWindowHandle);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                }
                finally
                {
                    p.Dispose();
                }
            }
            if (found.ProcessId != 0) return found;
        }
        return default;
    }

    private static nint FindClientHandle(Process process)
    {
        nint render = FindRenderHandle(process.Id);
        return render != 0 ? render : process.MainWindowHandle;
    }

    // TacO's GW2WindowFromPIDFunction uses these exact render classes.
    public static GameClientWindow FindRenderClient(uint linkedProcess)
    {
        var client = Find(linkedProcess);
        if (client.ProcessId == 0) return default;
        nint render = FindRenderHandle(client.ProcessId);
        return render == 0 ? default : new(client.ProcessId, render);
    }

    private delegate bool EnumerateWindow(nint hwnd, nint parameter);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumerateWindow callback, nint parameter);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hwnd, System.Text.StringBuilder name, int length);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);

    private static nint FindRenderHandle(int processId)
    {
        nint found = 0;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner != (uint)processId || !NativeGameWindow.IsWindowVisible(hwnd)) return true;
            var name = new System.Text.StringBuilder(256);
            GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() is not ("ArenaNet_Dx_Window_Class" or "ArenaNet_Gr_Window_Class")) return true;
            if (!NativeGameWindow.GetClientRect(hwnd, out var size) || size.Right < 64 || size.Bottom < 64) return true;
            found = hwnd;
            return false;
        }, 0);
        return found;
    }

    private static bool IsGame(string name) => name.Equals("Gw2-64", StringComparison.OrdinalIgnoreCase) || name.Equals("Gw2", StringComparison.OrdinalIgnoreCase) || name.Equals("GuildWars2", StringComparison.OrdinalIgnoreCase);

    public bool TryBounds(out NativeGameWindow.Rect rect)
    {
        rect = default;
        if (Handle == 0 || !NativeGameWindow.IsWindow(Handle) || NativeGameWindow.IsMinimizedRoot(Handle) || !NativeGameWindow.IsWindowVisible(Handle)) return false;
        if (!NativeGameWindow.GetClientRect(Handle, out var client)) return false;
        var origin = new NativeGameWindow.Point();
        if (!NativeGameWindow.ClientToScreen(Handle, ref origin)) return false;
        rect = new() { Left = origin.X, Top = origin.Y, Right = origin.X + client.Right, Bottom = origin.Y + client.Bottom };
        return rect.Right > rect.Left && rect.Bottom > rect.Top;
    }
}

public static class NativeGameWindow
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    public static extern bool IsHungAppWindow(nint hwnd);

    [DllImport("user32.dll")]
    public static extern nint GetWindowDpiAwarenessContext(nint hwnd);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    public static extern bool ClientToScreen(nint hwnd, ref Point point);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hwnd, uint command);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr64(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr64(nint hwnd, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(nint hwnd, int index, int value);

    public static void DisableActivation(nint hwnd, bool clickThrough = false)
    {
        const int extendedStyle = -20;
        uint noActivate = 0x08000000u | (clickThrough ? 0x20u : 0u);
        if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, extendedStyle, new nint(GetWindowLongPtr64(hwnd, extendedStyle).ToInt64() | (long)noActivate));
        else SetWindowLong32(hwnd, extendedStyle, unchecked((int)((uint)GetWindowLong32(hwnd, extendedStyle) | noActivate)));
    }

    public static bool IsTopmost(nint hwnd) => hwnd != 0 && IsWindow(hwnd)
        && ((IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, -20).ToInt64() : GetWindowLong32(hwnd, -20)) & 8) != 0;

    // Standalone overlays yield to the editor. In Play in App, both floating
    // controls and game-bound render surfaces stay above the app/game child.
    public static nint EditorOcclusionWindow { get; set; }
    public static bool HostedGameOverlay { get; set; }
    public static nint OcclusionFor(bool gameSurface) => HostedGameOverlay ? 0 : EditorOcclusionWindow;
    public static bool HasCorrectOverlayBand(nint hwnd, bool gameSurface) => IsTopmost(hwnd) == (OcclusionFor(gameSurface) == 0);

    public static bool PlaceOverlay(nint hwnd, nint above, int x, int y, int width, int height, bool show, bool gameSurface = false, bool keepAnchor = false)
    {
        // WPF popups live in separate HWNDs. Keep temporary UI above every
        // render surface as well as the controls, including during telemetry ticks.
        nint transient = 0, popupWindow = 0;
        foreach (System.Windows.Interop.HwndSource source in System.Windows.PresentationSource.CurrentSources.OfType<System.Windows.Interop.HwndSource>())
        {
            if (source.Handle == hwnd || !IsWindowVisible(source.Handle)) continue;
            bool popup = source.RootVisual?.GetType().Name == "PopupRoot";
            bool modal = source.RootVisual is System.Windows.Window w && w.Owner != null && !w.Owner.IsEnabled;
            if (!popup && !modal) continue;
            SetWindowPos(source.Handle, new nint(-1), 0, 0, 0, 0, 0x0013);
            transient = source.Handle;
            if (popup) popupWindow = source.Handle;
        }
        // Native file/folder pickers are owned modal windows too, but do not
        // appear in WPF's presentation sources. Protect the active picker.
        nint foreground = GetForegroundWindow();
        if (transient != 0 && foreground != hwnd && IsWindowVisible(foreground)
            && System.Windows.Interop.HwndSource.FromHwnd(foreground) == null && GetWindow(foreground, 4) != 0)
        {
            GetWindowThreadProcessId(foreground, out uint process);
            if (process == (uint)Environment.ProcessId)
            {
                SetWindowPos(foreground, new nint(-1), 0, 0, 0, 0, 0x0013);
                transient = foreground;
            }
        }
        if (popupWindow != 0)
        {
            SetWindowPos(popupWindow, new nint(-1), 0, 0, 0, 0, 0x0013);
            transient = popupWindow;
        }
        if (transient != 0 && (!gameSurface && !keepAnchor || above == 0 || !IsWindowVisible(above))) above = transient;
        nint editor = OcclusionFor(gameSurface);
        nint after;
        if (editor != 0 && IsWindowVisible(editor))
        {
            if (IsTopmost(hwnd) && !SetWindowPos(hwnd, new nint(-2), 0, 0, 0, 0, 0x0013)) return false;
            after = above != hwnd && above != 0 && IsWindowVisible(above) && !IsTopmost(above) ? above : IsTopmost(editor) ? 0 : editor;
        }
        else
        {
            if (!IsTopmost(hwnd) && !SetWindowPos(hwnd, new nint(-1), 0, 0, 0, 0, 0x0013)) return false;
            after = above != hwnd && above != 0 && IsWindowVisible(above) && IsTopmost(above) ? above : new nint(-1);
        }
        return SetWindowPos(hwnd, after, x, y, width, height, show ? 0x0050u : 0x0010u);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public uint Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern nint MonitorFromRect(ref Rect rect, uint flags);
    public static Rect MonitorWorkAreaAt(int x, int y, int width, int height, Rect fallback)
    {
        var bounds = new Rect { Left = x, Top = y, Right = (int)Math.Clamp((long)x + width, int.MinValue, int.MaxValue), Bottom = (int)Math.Clamp((long)y + height, int.MinValue, int.MaxValue) };
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfoW(MonitorFromRect(ref bounds, 2), ref info) ? info.Work : fallback;
    }
    public static Rect MonitorWorkArea(nint hwnd, Rect fallback)
    {
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfoW(MonitorFromWindow(hwnd, 2), ref info) ? info.Work : fallback;
    }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    public static extern nint GetCapture();

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    public static bool OtherButtonDown => (GetAsyncKeyState(GetSystemMetrics(23) != 0 ? 1 : 2) & 0x8000) != 0;
    public static bool EnterDown => (GetAsyncKeyState(0x0D) & 0x8000) != 0;
    public static bool EscapeDown => (GetAsyncKeyState(0x1B) & 0x8000) != 0;
    public static bool DeleteDown => (GetAsyncKeyState(0x2E) & 0x8000) != 0;
    public static bool ShiftDown => (GetAsyncKeyState(0x10) & 0x8000) != 0;
    public static bool ControlDown => (GetAsyncKeyState(0x11) & 0x8000) != 0;
    public static bool IsSelectButtonDown => (GetAsyncKeyState(GetSystemMetrics(23) != 0 ? 2 : 1) & 0x8000) != 0;
    public static void SetClickThrough(nint hwnd, bool enabled)
    {
        if (IntPtr.Size == 8)
        {
            long style = GetWindowLongPtr64(hwnd, -20).ToInt64();
            SetWindowLongPtr64(hwnd, -20, new nint(enabled ? style | 0x20 : style & ~0x20L));
        }
        else
        {
            int style = GetWindowLong32(hwnd, -20);
            SetWindowLong32(hwnd, -20, enabled ? style | 0x20 : style & ~0x20);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public Rect CaretBounds;
    }

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    private static extern bool IsChild(nint parent, nint child);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);

    public static bool IsMinimizedRoot(nint hwnd) => IsIconic(hwnd) || IsIconic(GetAncestor(hwnd, 2));

    public static bool IsGameRoot(GameClientWindow game, nint root) => game.Handle != 0 && root != 0
        && (root == game.Handle || root == GetAncestor(game.Handle, 2));

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint hwnd);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint first, uint second, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    // Called only after a deliberate toolbar interaction, never to steal focus
    // on mouse hover. Hosted games need keyboard focus on the render child too.
    public static bool FocusGameInput(GameClientWindow game)
    {
        if (game.Handle == 0 || !IsWindow(game.Handle) || !IsWindowVisible(game.Handle)) return false;
        GetWindowThreadProcessId(game.Handle, out uint process);
        if (process != (uint)game.ProcessId) return false;
        nint foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out uint foregroundProcess);
        if (!IsGameRoot(game, foreground) && foregroundProcess != (uint)Environment.ProcessId) return false;
        nint root = GetAncestor(game.Handle, 2);
        if (root == 0 || (foreground != root && !SetForegroundWindow(root))) return false;
        uint ownThread = GetCurrentThreadId(), gameThread = GetWindowThreadProcessId(game.Handle, out _);
        bool attached = ownThread != gameThread && AttachThreadInput(ownThread, gameThread, true);
        try
        {
            if (ownThread == gameThread || attached) SetFocus(game.Handle);
        }
        finally { if (attached) AttachThreadInput(ownThread, gameThread, false); }
        return IsGameInputActive(game);
    }

    public static bool IsGameInputActive(GameClientWindow game)
    {
        if (game.Handle == 0 || !IsWindow(game.Handle)) return false;
        nint foreground = GetForegroundWindow();
        if (foreground == game.Handle) return true;
        if (!IsGameRoot(game, foreground)) return false;
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        uint thread = GetWindowThreadProcessId(foreground, out _);
        return GetGUIThreadInfo(thread, ref info) && (info.Focus == game.Handle || IsChild(game.Handle, info.Focus));
    }

    private static readonly TacoStuffer.Models.OverlayForegroundState presentationForeground = new();
    public static nint PresentationForegroundWindow()
    {
        nint current = presentationForeground.Observe(GetForegroundWindow(), Environment.TickCount64);
        return current != 0 && IsWindow(current) && IsWindowVisible(current) ? current : 0;
    }

    public static bool IsForeground(GameClientWindow game, nint overlay, int editorProcess = 0)
    {
        nint foreground = PresentationForegroundWindow();
        if (foreground == 0) return false;
        if (foreground == game.Handle) return true;
        GetWindowThreadProcessId(foreground, out uint foregroundProcess);
        if (editorProcess > 0 && foregroundProcess == (uint)editorProcess) return true;
        // Other apps still hide the overlay; editor/owned windows are allowed.
        for (int depth = 0; overlay != 0 && foreground != 0 && depth < 8; depth++, foreground = GetWindow(foreground, 4))
            if (foreground == overlay) return true;
        return false;
    }
}

public sealed class GameWindowEvents : IDisposable
{
    private delegate void WinEvent(nint hook, uint kind, nint hwnd, int objectId, int childId, uint thread, uint time);
    private readonly WinEvent callback;
    private readonly nint foregroundHook;
    private readonly nint minimizeHook;
    public event Action? Changed;
    public GameWindowEvents()
    {
        callback = (_, _, _, _, _, _, _) => Changed?.Invoke();
        foregroundHook = SetWinEventHook(3, 3, 0, callback, 0, 0, 0);
        minimizeHook = SetWinEventHook(0x16, 0x17, 0, callback, 0, 0, 0);
    }

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEvent callback, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(nint hook);

    public void Dispose()
    {
        if (foregroundHook != 0) UnhookWinEvent(foregroundHook);
        if (minimizeHook != 0) UnhookWinEvent(minimizeHook);
    }
}
