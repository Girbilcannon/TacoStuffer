using System.Numerics;
using System.Runtime.InteropServices;
using TacoStuffer.Models;

namespace TacoStuffer.Services;

// Passive native observation. Geometry/UI work stays in queued consumers; mouse
// messages are never swallowed here, allowing normal GW2 camera gestures.
public sealed class OverlayMouseObserver : IDisposable
{
    private delegate nint HookProc(int code, nint message, nint data);
    private readonly HookProc callback; private nint hook;
    private readonly OverlayClickGesture gesture = new();
    private NativeGameWindow.Point origin; private nint originRoot; private bool shift, control, other;
    public bool IsActive => hook != 0;
    public bool OtherDown => other;
    public event Action<bool, NativeGameWindow.Point, nint>? Button;
    public event Action<OverlayClick>? Click;
    public OverlayMouseObserver() { callback = Observe; hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0); }
    private nint Observe(int code, nint message, nint data)
    {
        if (code >= 0 && (long)message is >= 0x200 and <= 0x206)
        {
            var point = Marshal.PtrToStructure<MouseData>(data).Point;
            bool swapped = GetSystemMetrics(23) != 0;
            int down = swapped ? 0x204 : 0x201, up = swapped ? 0x205 : 0x202, otherDown = swapped ? 0x201 : 0x204, otherUp = swapped ? 0x202 : 0x205;
            var p = new Vector2(point.X, point.Y);
            if (message == 0x200) gesture.Move(p);
            else if (message == otherDown) { other = true; gesture.OtherButton(); }
            else if (message == otherUp) other = false;
            else if (message == down)
            {
                origin = point; originRoot = GetAncestor(WindowFromPoint(point), 2);
                shift = (GetAsyncKeyState(0x10) & 0x8000) != 0; control = (GetAsyncKeyState(0x11) & 0x8000) != 0;
                other |= (GetAsyncKeyState(swapped ? 1 : 2) & 0x8000) != 0;
                gesture.Begin(p, Environment.TickCount64, other);
                Button?.Invoke(true, point, originRoot);
            }
            else if (message == up)
            {
                bool click = gesture.End(p, Environment.TickCount64, other);
                Button?.Invoke(false, point, GetAncestor(WindowFromPoint(point), 2));
                if (click) Click?.Invoke(new(origin.X, origin.Y, point.X, point.Y, shift, control, originRoot));
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }
    public void Dispose() { if (hook != 0) { UnhookWindowsHookEx(hook); hook = 0; } gesture.Cancel(); }
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public NativeGameWindow.Point Point; public uint Mouse, Flags, Time; public nuint Extra; }
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativeGameWindow.Point p);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int key);
}
