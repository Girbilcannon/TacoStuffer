using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TacoStuffer.Models;

namespace TacoStuffer;

public partial class MainWindow
{
    private EditorPreferences preferences = ReadPreferences();
    private readonly HashSet<int> heldShortcutKeys = [];
    private static EditorPreferences ReadPreferences()
    {
        try
        {
            return EditorPreferences.Load(PreferenceFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            return new();
        }
    }

    private bool CustomShortcut(KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase || codePages.Any(p => p.Editor.IsKeyboardFocusWithin)) return false;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        int code = KeyInterop.VirtualKeyFromKey(key), modifiers = (int)Keyboard.Modifiers;
        var binding = preferences.Keybinds.FirstOrDefault(p => p.Value.VirtualKey == code && p.Value.Modifiers == modifiers);
        if (binding.Key == null) return false;
        e.Handled = true;
        if (!e.IsRepeat) ExecuteCustomAction(binding.Key);
        return true;
    }

    private void ExecuteCustomAction(string action)
    {
        if (workspaceBusy || projectFile == null) return;
        if (action == "interact") { CopyNearbyMarker(); return; }
        FlushHistoryCheckpoint();
        if (action == "category" && (overlay == null || !overlay.IsRenderingActive || (IsActive && !Services.NativeGameWindow.IsGameInputActive(gameClient))))
        {
            AddCategory(this, new RoutedEventArgs());
            return;
        }
        if (overlay?.IsRenderingActive == true) overlay.InvokeShortcut(action);
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private bool pollingShortcuts;
    private void PollGameShortcuts(bool enabled)
    {
        enabled &= !workspaceBusy;
        if (pollingShortcuts) return;
        pollingShortcuts = true;
        try
        {
            PollGameShortcutEdges(enabled);
        }
        finally
        {
            pollingShortcuts = false;
        }
    }

    private void PollGameShortcutEdges(bool enabled)
    {
        // Observe edges while inactive so returning with a held key cannot
        // unexpectedly trigger an operation. Never consume game key messages.
        var keys = preferences.Keybinds.Values.Select(k => k.VirtualKey).Append(90).Distinct().ToList();
        int modifiers = (GetAsyncKeyState(0x12) < 0 ? 1 : 0) | (GetAsyncKeyState(0x11) < 0 ? 2 : 0) | (GetAsyncKeyState(0x10) < 0 ? 4 : 0)
            | (GetAsyncKeyState(0x5b) < 0 || GetAsyncKeyState(0x5c) < 0 ? 8 : 0);
        foreach (int key in keys)
        {
            bool down = GetAsyncKeyState(key) < 0;
            if (!down)
            {
                heldShortcutKeys.Remove(key);
                continue;
            }
            if (!heldShortcutKeys.Add(key) || !enabled) continue;
            if (key == 90 && modifiers is 2 or 6)
            {
                if (modifiers == 6) RedoProject();
                else UndoProject();
                continue;
            }
            var action = preferences.Keybinds.FirstOrDefault(p => p.Value.VirtualKey == key && p.Value.Modifiers == modifiers).Key;
            if (action != null) ExecuteCustomAction(action);
        }
    }
}
