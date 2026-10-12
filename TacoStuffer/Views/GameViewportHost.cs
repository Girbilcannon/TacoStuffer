using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using TacoStuffer.Services;

namespace TacoStuffer.Views;

// WPF owns only the native viewport container. GW2 is parented to it;
// parent clipping hides the game frame without changing its window region.
public sealed class GameViewportHost : HwndHost
{
    private const int FrameOverlap = 1; // physical pixel beneath each side of the app frame
    private const string ContainerClass = "TacoStuffer.DarkGameViewport";
    private delegate nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam);
    private static readonly WindowProcedure ContainerProcedure = DefWindowProc;
    private static ushort containerClass;
    private static void RegisterContainer()
    {
        if (containerClass != 0) return;
        nint brush = CreateSolidBrush(0x00201911); // COLORREF for #111920
        if (brush == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        var definition = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(), Instance = GetModuleHandle(null),
            Procedure = Marshal.GetFunctionPointerForDelegate(ContainerProcedure),
            Background = brush, ClassName = ContainerClass
        };
        containerClass = RegisterClassEx(ref definition);
        if (containerClass == 0)
        {
            int error = Marshal.GetLastPInvokeError();
            DeleteObject(brush);
            throw new Win32Exception(error);
        }
        // The registered class owns its brush until process shutdown.
    }

    private nint container, game, originalParent, originalOwner, originalStyle, originalExtendedStyle;
    private int processId, lastWidth, lastHeight, requestedWidth, requestedHeight;
    private readonly DispatcherTimer resizeTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private long lastGeometryRepair;
    private int geometryRepairs, parentRepairs;
    private long lastParentRepair;
    private (int Width, int Height, int X, int Y)? repairTarget;
    private nint originalGameDpi;
    private uint originalParentThread, originalParentProcess, originalOwnerThread, originalOwnerProcess;
    private bool releaseInProgress, hostUpdatesSuspended;
    private bool mixedDpiContainer;
    public GameViewportHost()
    {
        resizeTimer.Tick += (_, _) =>
        {
            resizeTimer.Stop();
            ApplyViewportSize();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!hostUpdatesSuspended && IsAttached) ShowWindow(game, IsVisible ? 5 : 0);
        };
    }

    private NativeGameWindow.Rect originalBounds;
    private Placement originalPlacement;
    private bool hasPlacement;
    public GameClientWindow Client
    {
        get
        {
            if (game == 0 || !NativeGameWindow.IsWindow(game)) return default;
            GetWindowThreadProcessId(game, out uint owner);
            return owner == (uint)processId ? new(processId, game) : default;
        }
    }
    public bool IsAttached => Client.Handle != 0;
    public string? LastError { get; private set; }
    public void SuspendHosting()
    {
        hostUpdatesSuspended = true;
        resizeTimer.Stop();
    }

    public void ResumeHosting()
    {
        if (!releaseInProgress) hostUpdatesSuspended = false;
    }

    public void ForgetExitedClient()
    {
        // Caller has observed exit on the retained GW2 process handle.
        resizeTimer.Stop();
        CompleteRelease();
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        RegisterContainer();
        // Opt only this native container into mixed-DPI hosting. Do not change
        // the editor/game process DPI modes or leave thread settings changed.
        int previousHosting = -1;
        int createError = 0;
        try
        {
            try
            {
                previousHosting = SetThreadDpiHostingBehavior(1);
                mixedDpiContainer = previousHosting >= 0;
            }
            catch (EntryPointNotFoundException)
            {
            }
            container = CreateWindowEx(0, ContainerClass, "Taco Stuffer Game Viewport", 0x46000000, 0, 0, 0, 0, hwndParent.Handle, 0, GetModuleHandle(null), 0);
            if (container == 0) createError = Marshal.GetLastPInvokeError();
        }
        finally
        {
            if (previousHosting >= 0) SetThreadDpiHostingBehavior(previousHosting);
        }
        if (container == 0) throw new Win32Exception(createError);
        return new HandleRef(this, container);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        resizeTimer.Stop();
        // Failure must never cause DestroyWindow to destroy an attached client.
        if (Detach())
        {
            DestroyWindow(hwnd.Handle);
            container = 0;
        }
        else System.Diagnostics.Debug.WriteLine("GW2 viewport release failed: " + LastError);
    }

    protected override void OnWindowPositionChanged(Rect bounds)
    {
        base.OnWindowPositionChanged(bounds);
        ResizeGame();
    }

    protected override nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        nint result = base.WndProc(hwnd, message, wParam, lParam, ref handled);
        if (message == 5) ResizeGame(); // WM_SIZE, after the container's native bounds change
        return result;
    }

    protected override bool HasFocusWithinCore() => NativeGameWindow.IsGameInputActive(Client);

    protected override bool TabIntoCore(System.Windows.Input.TraversalRequest request) => FocusGame();

    public bool FocusGame()
    {
        if (hostUpdatesSuspended || !IsAttached || !IsVisible) return false;
        var editor = Window.GetWindow(this);
        if (editor != null) SetForegroundWindow(new WindowInteropHelper(editor).Handle);
        SetFocus(game);
        return NativeGameWindow.IsGameInputActive(Client);
    }

    public void ResizeGame()
    {
        if (hostUpdatesSuspended || !IsAttached || container == 0 || !NativeGameWindow.GetClientRect(container, out var bounds)) return;
        // Never deliver minimized, zero or transient tiny layout sizes to GW2.
        if (bounds.Right < 128 || bounds.Bottom < 128) return;
        if (requestedWidth == bounds.Right && requestedHeight == bounds.Bottom)
        {
            if (lastWidth == bounds.Right && lastHeight == bounds.Bottom || resizeTimer.IsEnabled) return;
        }
        else geometryRepairs = 0;
        requestedWidth = bounds.Right;
        requestedHeight = bounds.Bottom;
        resizeTimer.Stop();
        resizeTimer.Start();
    }

    private bool ClientInsets(out int left, out int top, out int right, out int bottom)
    {
        left = top = right = bottom = 0;
        if (!NativeGameWindow.GetWindowRect(game, out var window) || !NativeGameWindow.GetClientRect(game, out var client)) return false;
        var origin = new NativeGameWindow.Point();
        if (!NativeGameWindow.ClientToScreen(game, ref origin)) return false;
        left = Math.Clamp(origin.X - window.Left, 0, 128);
        top = Math.Clamp(origin.Y - window.Top, 0, 128);
        right = Math.Clamp(window.Right - origin.X - client.Right, 0, 128);
        bottom = Math.Clamp(window.Bottom - origin.Y - client.Bottom, 0, 128);
        return true;
    }

    // Resize is debounced to avoid sending every transient splitter size to GW2.
    // Coordinates and frame insets below are native pixels, not WPF layout units.
    private void ApplyViewportSize()
    {
        if (hostUpdatesSuspended || !IsAttached || !IsVisible || !NativeGameWindow.GetClientRect(container, out var bounds)
            || bounds.Right < 128 || bounds.Bottom < 128 || !ClientInsets(out int left, out int top, out int right, out int bottom)) return;
        requestedWidth = bounds.Right;
        requestedHeight = bounds.Bottom;
        // GW2 may restore a custom frame. Size for the CLIENT area and hide its
        // non-client border through native parent clipping.
        if (!EnsureNativeParent() || !ViewportPosition(left, top, out int x, out int y)) return;
        if (!NativeGameWindow.SetWindowPos(game, 0, x, y, bounds.Right + left + right + FrameOverlap * 2, bounds.Bottom + top + bottom + FrameOverlap * 2, 0x4014))
        {
            LastError = "Windows rejected the viewport resize.";
            return;
        }
        lastWidth = bounds.Right;
        lastHeight = bounds.Bottom;
        lastGeometryRepair = Environment.TickCount64;

    }

    public void MaintainFrame()
    {
        if (hostUpdatesSuspended || !IsAttached) return;
        bool visible = IsVisible && NativeGameWindow.IsWindowVisible(container) && !NativeGameWindow.IsMinimizedRoot(container);
        if (!visible)
        {
            if (NativeGameWindow.IsWindowVisible(game)) ShowWindow(game, 0);
            return;
        }
        if (!EnsureNativeParent()) return;
        if (!NativeGameWindow.IsWindowVisible(game)) ShowWindow(game, 5);
        if (resizeTimer.IsEnabled) return;
        if (!NativeGameWindow.GetClientRect(container, out var host) || host.Right < 128 || host.Bottom < 128
            || !NativeGameWindow.GetClientRect(game, out var client) || !ClientInsets(out int left, out int top, out _, out _)) return;
        var gameOrigin = new NativeGameWindow.Point();
        var hostOrigin = new NativeGameWindow.Point();
        if (!NativeGameWindow.ClientToScreen(game, ref gameOrigin) || !NativeGameWindow.ClientToScreen(container, ref hostOrigin)) return;
        if (client.Right == host.Right + FrameOverlap * 2 && client.Bottom == host.Bottom + FrameOverlap * 2)
        {
            if (gameOrigin.X != hostOrigin.X - FrameOverlap || gameOrigin.Y != hostOrigin.Y - FrameOverlap)
            {
                if (!ViewportPosition(left, top, out int x, out int y)) return;
                // Moving the editor must not resize GW2's render buffers.
                NativeGameWindow.SetWindowPos(game, 0, x, y, 0, 0, 0x4015);
            }
            return;
        }
        if (Environment.TickCount64 - lastGeometryRepair < 800) return;
        var target = (host.Right, host.Bottom, hostOrigin.X, hostOrigin.Y);
        if (repairTarget != target)
        {
            repairTarget = target;
            geometryRepairs = 0;
        }
        if (geometryRepairs++ >= 3)
        {
            LastError = "GW2 is overriding its hosted window size. Return to Desktop; details are recorded in game-hosting.log.";
            return;
        }
        lastWidth = lastHeight = 0;
        ResizeGame();
    }

    private bool ViewportPosition(int left, int top, out int x, out int y)
    {
        x = y = 0;
        var target = new NativeGameWindow.Point();
        if (!NativeGameWindow.ClientToScreen(container, ref target)) return false;
        target.X -= left + FrameOverlap;
        target.Y -= top + FrameOverlap;
        // Use the real parent, excluding owner. GW2 rewrites its STYLE bits;
        // those bits alone previously made us add the viewport offset twice.
        nint parent = GetAncestor(game, 1);
        if (parent != 0 && parent != GetDesktopWindow())
        {
            Marshal.SetLastPInvokeError(0);
            int mapped = MapWindowPoints(0, parent, ref target, 1);
            if (mapped == 0 && Marshal.GetLastPInvokeError() != 0)
            {
                LastError = "Could not map the game position into its native parent.";
                return false;
            }
        }
        x = target.X;
        y = target.Y;
        return true;
    }

    private bool EnsureNativeParent()
    {
        if (hostUpdatesSuspended) return false;
        try
        {
            if (container == 0 || !NativeGameWindow.IsWindow(container))
            {
                LastError = "The game viewport is unavailable.";
                return false;
            }
            if (GetAncestor(game, 1) != container)
            {
                long now = Environment.TickCount64;
                if (lastParentRepair != 0 && now - lastParentRepair < 500) return false;
                if (parentRepairs++ >= 3)
                {
                    LastError = "GW2 repeatedly removed its native parent link. Return to Desktop; details are in game-hosting.log.";
                    return false;
                }
                lastParentRepair = now;
                long style = ReadLong(game, -16).ToInt64();
                WriteLong(game, -16, new nint((style & ~0x80000000L) | 0x40000000L));
                Reparent(game, container);
            }
            if (GetAncestor(game, 1) != container)
            {
                LastError = "Windows did not establish the game viewport parent link.";
                return false;
            }
            if (Environment.TickCount64 - lastParentRepair > 2000) parentRepairs = 0;
            return true;
        }
        catch (Win32Exception ex)
        {
            LastError = "Could not parent the game viewport: " + ex.Message;
            return false;
        }
    }

    // Hosting changes a foreign native window. Capture its original state before
    // reparenting so detach can restore it; keep the editor container alive while
    // that client remains attached. A WPF panel is not a fullscreen display output.
    public bool Attach(GameClientWindow client)
    {
        if (hostUpdatesSuspended && IsAttached)
        {
            LastError = "Finish Return to Desktop before attaching again.";
            return false;
        }
        hostUpdatesSuspended = false;
        LastError = null;
        if (IsAttached) return Client == client;
        if (container == 0)
        {
            LastError = "The Game tab is not ready yet. Try Attach again.";
            return false;
        }
        if (client.Handle == 0 || !NativeGameWindow.IsWindow(client.Handle))
        {
            LastError = "No playable game window is available.";
            return false;
        }
        GetWindowThreadProcessId(client.Handle, out uint owner);
        if (owner != (uint)client.ProcessId || owner == (uint)Environment.ProcessId)
        {
            LastError = "The game window changed. Try again.";
            return false;
        }
        originalStyle = ReadLong(client.Handle, -16);
        // GW2 uses a custom render window. Caption bits do not prove its display mode.
        // The caller identifies the ArenaNet render class and asks for Windowed mode.
        game = client.Handle;
        processId = client.ProcessId;
        // Read the real parent independently of GW2's mutable style bits.
        // GetParent may instead return an owner (such as the launcher).
        originalParent = GetAncestor(game, 1);
        if (originalParent == GetDesktopWindow()) originalParent = 0;
        originalOwner = originalParent == 0 ? GetWindow(game, 4) : 0;
        originalParentThread = GetWindowThreadProcessId(originalParent, out originalParentProcess);
        originalOwnerThread = GetWindowThreadProcessId(originalOwner, out originalOwnerProcess);
        lastWidth = lastHeight = requestedWidth = requestedHeight = 0;
        geometryRepairs = parentRepairs = 0;
        lastParentRepair = 0;
        repairTarget = null;
        originalGameDpi = GetWindowDpiAwarenessContext(game);
        originalExtendedStyle = ReadLong(game, -20);
        if (!NativeGameWindow.GetWindowRect(game, out originalBounds))
        {
            game = 0;
            processId = 0;
            LastError = "Could not read GW2's window position. Try attaching again.";
            return false;
        }
        originalPlacement = new() { Length = (uint)Marshal.SizeOf<Placement>() };
        hasPlacement = GetWindowPlacement(game, ref originalPlacement);
        if (!NativeGameWindow.GetClientRect(container, out var viewport) || viewport.Right < 128 || viewport.Bottom < 128)
        {
            game = 0;
            processId = 0;
            LastError = "The viewport does not have a usable size yet. Try Attach again.";
            return false;
        }
        try
        {
            // Return to the child-hosting path that rendered successfully in
            // 0.0.45. Keep frame styles; move the frame beyond the parent's
            // visible client area instead of clipping GW2 with SetWindowRgn.
            if (!EnsureNativeParent()) throw new Win32Exception(LastError ?? "Could not parent the game window.");
            if (!ClientInsets(out int left, out int top, out int right, out int bottom)
                || !ViewportPosition(left, top, out int x, out int y))
                throw new Win32Exception("Could not measure the game viewport.");
            if (!NativeGameWindow.SetWindowPos(game, 0, x, y,
                viewport.Right + left + right + FrameOverlap * 2, viewport.Bottom + top + bottom + FrameOverlap * 2, 0x4050))
                throw new Win32Exception("Windows rejected the initial hosted game size.");
            requestedWidth = lastWidth = viewport.Right;
            requestedHeight = lastHeight = viewport.Bottom;
            lastGeometryRepair = Environment.TickCount64;
            ShowWindow(game, 5);
            return true;
        }
        catch (Win32Exception ex)
        {
            LastError = "Could not host GW2: " + ex.Message;
            string error = LastError;
            if (!Detach()) error += " Restore also failed: " + LastError;
            LastError = error;
            return false;
        }
    }

    // Release is distinct from process exit: restore only a live client and valid
    // saved handles. Viewport maintenance must not race these restoration steps.
    public bool Detach()
    {
        if (releaseInProgress) return false;
        // SetParent and frame changes can dispatch native window messages.
        // Neither those callbacks nor polling may re-host GW2 during release.
        hostUpdatesSuspended = true;
        resizeTimer.Stop();
        if (!IsAttached)
        {
            CompleteRelease();
            return true;
        }
        releaseInProgress = true;
        string step = "restore parent";
        try
        {
            nint parent = SavedWindowIsAlive(originalParent, originalParentThread, originalParentProcess) ? originalParent : 0;
            try
            {
                Reparent(game, parent);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1400 && IsAttached && parent != 0
                && !SavedWindowIsAlive(parent, originalParentThread, originalParentProcess))
            {
                parent = 0;
                Reparent(game, 0);
            }
            nint expectedParent = parent == 0 ? GetDesktopWindow() : parent;
            if (GetAncestor(game, 1) != expectedParent)
                throw new Win32Exception(5023, "The game did not leave its hosted parent. Try Return to Desktop again.");
            step = "restore window style";
            // SetParent does not update WS_CHILD/WS_POPUP. A desktop window
            // must no longer be a child, even if GW2 saved that bit itself.
            long style = originalStyle.ToInt64();
            style = parent == 0
                ? (style & ~0x40000000L) | ((style & 0x40000000L) != 0 ? 0x80000000L : 0)
                : (style & ~0x80000000L) | 0x40000000L;
            WriteLong(game, -16, new nint(style));
            step = "restore extended window style";
            WriteLong(game, -20, originalExtendedStyle);
            if (parent == 0)
            {
                step = "restore window owner";
                nint owner = SavedWindowIsAlive(originalOwner, originalOwnerThread, originalOwnerProcess) ? originalOwner : 0;
                try
                {
                    if (GetWindow(game, 4) != owner) WriteLong(game, -8, owner);
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1400 && IsAttached && owner != 0
                    && !SavedWindowIsAlive(owner, originalOwnerThread, originalOwnerProcess))
                {
                    WriteLong(game, -8, 0);
                }
            }
            step = "restore desktop bounds";
            int x = originalBounds.Left, y = originalBounds.Top;
            if (parent != 0)
            {
                var origin = new NativeGameWindow.Point { X = x, Y = y };
                Marshal.SetLastPInvokeError(0);
                int mapped = MapWindowPoints(0, parent, ref origin, 1);
                int error = Marshal.GetLastPInvokeError();
                if (mapped == 0 && error != 0) throw new Win32Exception(error);
                x = origin.X;
                y = origin.Y;
            }
            if (!RestoreWindowBounds(game, 0, x, y,
                Math.Max(1, originalBounds.Right - originalBounds.Left), Math.Max(1, originalBounds.Bottom - originalBounds.Top), 0x0034))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            step = "restore window placement";
            if (parent == 0 && hasPlacement && !SetWindowPlacement(game, ref originalPlacement))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            ShowWindow(game, hasPlacement ? (int)originalPlacement.ShowCommand : 5);
            CompleteRelease();
            return true;
        }
        catch (Win32Exception ex)
        {
            if (!IsAttached)
            {
                CompleteRelease();
                return true;
            }
            // Keep updates suspended on failure: polling must not reattach a
            // partially released window. Return to Desktop can safely retry.
            LastError = $"Could not release GW2 ({step}, Win32 {ex.NativeErrorCode}): {ex.Message}";
            return false;
        }
        finally
        {
            releaseInProgress = false;
        }
    }

    private void CompleteRelease()
    {
        lastWidth = lastHeight = requestedWidth = requestedHeight = 0;
        game = 0;
        processId = 0;
        LastError = null;
        hostUpdatesSuspended = false;
    }

    private static bool SavedWindowIsAlive(nint hwnd, uint thread, uint process)
    {
        if (hwnd == 0 || thread == 0 || !NativeGameWindow.IsWindow(hwnd)) return false;
        uint currentThread = GetWindowThreadProcessId(hwnd, out uint currentProcess);
        return currentThread == thread && currentProcess == process;
    }

    public void HideContainer()
    {
        resizeTimer.Stop();
        if (container != 0) ShowWindow(container, 0);
    }

    public string DescribeClient(GameClientWindow client)
    {
        nint style = client.Handle != 0 ? ReadLong(client.Handle, -16) : 0;
        string sizes = NativeGameWindow.GetClientRect(client.Handle, out var rect) ? $"{rect.Right}x{rect.Bottom}" : "unknown";
        return $"client {sizes}; mixed-DPI container={mixedDpiContainer}; original game DPI context 0x{originalGameDpi.ToInt64():X}; current game DPI context 0x{GetWindowDpiAwarenessContext(client.Handle).ToInt64():X}; host DPI context 0x{GetWindowDpiAwarenessContext(container).ToInt64():X}; DPI contexts match={AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(client.Handle), GetWindowDpiAwarenessContext(container))}; PID {client.ProcessId}; game HWND 0x{client.Handle.ToInt64():X}; host HWND 0x{container.ToInt64():X}; actual parent 0x{GetAncestor(client.Handle, 1).ToInt64():X}; native child={IsChild(container, client.Handle)}; actual owner 0x{GetWindow(client.Handle, 4).ToInt64():X}; style 0x{style.ToInt64():X}; parent 0x{(client.Handle != 0 ? GetParent(client.Handle) : 0).ToInt64():X}";
    }

    private static void Reparent(nint hwnd, nint parent)
    {
        Marshal.SetLastPInvokeError(0);
        nint previous = SetParent(hwnd, parent);
        int error = Marshal.GetLastPInvokeError();
        if (previous == 0 && error != 0) throw new Win32Exception(error);
    }

    private static nint ReadLong(nint hwnd, int index) => IntPtr.Size == 8 ? GetWindowLongPtr(hwnd, index) : new nint(GetWindowLong(hwnd, index));

    private static void WriteLong(nint hwnd, int index, nint value)
    {
        Marshal.SetLastPInvokeError(0);
        nint previous = IntPtr.Size == 8 ? SetWindowLongPtr(hwnd, index, value) : new nint(SetWindowLong(hwnd, index, unchecked((int)value.ToInt64())));
        int error = Marshal.GetLastPInvokeError();
        if (previous == 0 && error != 0) throw new Win32Exception(error);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style;
        public nint Procedure;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClass definition);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint GetWindowDpiAwarenessContext(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int SetThreadDpiHostingBehavior(int behavior);

    [DllImport("user32.dll")]
    private static extern bool AreDpiAwarenessContextsEqual(nint first, nint second);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint handle);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? module);

    [StructLayout(LayoutKind.Sequential)]
    private struct Placement
    {
        public uint Length, Flags, ShowCommand;
        public NativeGameWindow.Point MinPosition, MaxPosition;
        public NativeGameWindow.Rect NormalPosition;
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint hwnd, nint parent);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsChild(nint parent, nint child);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int MapWindowPoints(nint from, nint to, ref NativeGameWindow.Point point, uint count);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hwnd, uint command);

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(nint hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(nint hwnd, ref Placement placement);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPlacement(nint hwnd, ref Placement placement);

    [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    private static extern bool RestoreWindowBounds(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);
}
