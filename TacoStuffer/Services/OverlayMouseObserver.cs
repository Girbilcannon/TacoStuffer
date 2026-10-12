using System.Runtime.InteropServices;

namespace TacoStuffer.Services;

// Passive button observation while the world overlay exists. Never consumes or
// changes input; consumers must queue work and return immediately.
public sealed class OverlayMouseObserver : IDisposable
{
    private delegate nint HookProc(int code, nint message, nint data);
    private readonly HookProc callback;
    private nint hook;
    public bool IsActive => hook != 0;
    public event Action<bool, NativeGameWindow.Point, nint>? Button;
    public OverlayMouseObserver()
    {
        callback = Observe;
        hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
    }

    private nint Observe(int code, nint message, nint data)
    {
        if (code >= 0 && (message == 0x201 || message == 0x202))
        {
            var point = Marshal.PtrToStructure<MouseData>(data).Point;
            nint root = GetAncestor(WindowFromPoint(point), 2);
            Button?.Invoke(message == 0x201, point, root);
        }
        return CallNextHookEx(hook, code, message, data);
    }

    public void Dispose()
    {
        if (hook != 0)
        {
            UnhookWindowsHookEx(hook);
            hook = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseData
    {
        public NativeGameWindow.Point Point;
        public uint Mouse, Flags, Time;
        public nuint Extra;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativeGameWindow.Point point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);
}
