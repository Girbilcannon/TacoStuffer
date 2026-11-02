using System.Runtime.InteropServices;

namespace TacoStuffer.Services;

// Only Enter/Esc belonging to an active world tool are captured. This avoids
// opening game chat when Enter commits a merge. Ordinary typing is untouched.
public sealed class OverlayToolKeys : IDisposable
{
    private delegate nint HookProc(int code, nint message, nint data);
    private readonly HookProc callback;
    private readonly Func<int, bool> shouldCapture;
    private readonly HashSet<int> held = [];
    private nint hook;
    public bool IsActive => hook != 0;
    public event Action<int>? Pressed;
    public OverlayToolKeys(Func<int, bool> capture)
    { shouldCapture = capture; callback = Observe; hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0); }
    private nint Observe(int code, nint message, nint data)
    {
        if (code >= 0 && (message == 0x100 || message == 0x104 || message == 0x101 || message == 0x105))
        {
            int key = Marshal.ReadInt32(data);
            if (key is 0x0D or 0x1B)
            {
                bool down = message == 0x100 || message == 0x104;
                if (!down && held.Remove(key)) return 1;
                if (down && held.Contains(key)) return 1;
                if (down && shouldCapture(key)) { held.Add(key); Pressed?.Invoke(key); return 1; }
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }
    public void Dispose() { if (hook != 0) { UnhookWindowsHookEx(hook); hook = 0; } held.Clear(); }
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
