using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using TacoStuffer.Models;
using TacoStuffer.Services;
using TacoStuffer.Views;

namespace TacoStuffer;

public partial class MainWindow
{
    private readonly MarkerInteractionSession interactionSession = new();
    private List<MarkerInteraction> markerInteractions = [];
    private InteractionPreview interactionPreview = new(null, null, null);
    private InteractionHudWindow? infoHud, copyHud;
    private string? shownInfoKey;
    private string? dismissedCopyKey;
    private uint interactionMap;
    private bool interactionTestMode;
    private long confirmationUntil;
    private string confirmationText = "";
    private TextBlock? interactionRangeStatus;
    private EditorPreferences InteractionPreferences => visualPreviewPreferences ?? preferences;

    private void RebuildInteractions()
    {
        var categories = CategoryValues();
        markerInteractions = pages.SelectMany(p => p.Xml.Descendants("POI").Select(e => MarkerInteraction.Read(p.Name + "|" + ElementKey(e), e, categories)))
            .OfType<MarkerInteraction>().ToList();
        // Editing/importing must never count as a new clipboard-trigger entry.
        interactionSession.Reset();
        interactionPreview = new(null, null, null);
        dismissedCopyKey = null;
        shownInfoKey = null;
        confirmationUntil = 0;
        infoHud?.Hide();
        copyHud?.Hide();
    }

    private void CloseInteractionHuds()
    {
        infoHud?.Close(); copyHud?.Close(); infoHud = copyHud = null;
        shownInfoKey = null;
        dismissedCopyKey = null;
        interactionSession.Reset();
        interactionPreview = new(null, null, null);
        interactionTestMode = false;
        interactionMap = 0;
        confirmationUntil = 0;
    }

    private bool CanInteractInWorld() => overlay?.IsRenderingActive == true && mumbleSession.IsPlayable && gameFrame != null
        && gameFrame.ProcessId == (uint)overlay.BoundProcessId && (gameFrame.UiState & 33) == 0
        && NativeGameWindow.IsGameInputActive(gameClient); // Full map = bit 0; chat/text input = bit 5.

    private void UpdateInteractionPreview()
    {
        if (gameFrame == null || !mumbleSession.IsPlayable || overlay == null)
        {
            infoHud?.Hide(); copyHud?.Hide(); interactionSession.Reset();
            interactionPreview = new(null, null, null);
            interactionMap = 0;
            confirmationUntil = 0;
            if (interactionRangeStatus != null) interactionRangeStatus.Text = "Waiting for live game-overlay telemetry.";
            return;
        }
        if (interactionMap != gameFrame.MapId)
        {
            interactionMap = gameFrame.MapId;
            confirmationUntil = 0;
            dismissedCopyKey = shownInfoKey = null;
        }
        long now = Environment.TickCount64;
        bool input = CanInteractInWorld();
        var visible = markerInteractions.Where(m => PreviewCategoryVisible(m.Category)).ToList();
        interactionPreview = interactionSession.Update(visible, gameFrame.MapId, gameFrame.AvatarPosition, now,
            input && interactionTestMode && InteractionPreferences.MarkerClipboard == MarkerClipboardMode.Always);
        if (interactionPreview.Copy?.Key != dismissedCopyKey) dismissedCopyKey = null;
        if (interactionRangeStatus != null && selected?.Element is { } element)
        {
            var marker = MarkerInteraction.Read(selected.DocumentName + "|" + selected.Key, element, CategoryValues());
            if (marker != null)
            {
                double distance = System.Numerics.Vector3.Distance(marker.Position, gameFrame.AvatarPosition);
                interactionRangeStatus.Text = marker.MapId != gameFrame.MapId ? "Marker is on another map."
                    : $"{(distance <= marker.CopyRadius ? "In range" : "Outside range")} · {distance:0.0} m / {marker.CopyRadius:0.##} m";
            }
        }
        if (interactionPreview.Automatic is { } automatic)
        {
            interactionSession.AutomaticAttempted(now);
            TryMarkerCopy(automatic.Copy, automatic.Message);
        }
        if (!input || !gameClient.TryBounds(out var bounds)) { infoHud?.Hide(); copyHud?.Hide(); return; }
        if (InteractionPreferences.ShowMarkerInformation && interactionPreview.Info is { } info)
        {
            if (infoHud == null)
            {
                infoHud = new InteractionHudWindow(true) { Owner = overlay };
                infoHud.Dismissed += () => { if (shownInfoKey != null) interactionSession.Dismiss(shownInfoKey); };
            }
            shownInfoKey = info.Key;
            infoHud.SetText(info.Info);
            infoHud.Present(bounds, overlay.Handle, true);
        }
        else infoHud?.Hide();
        bool confirming = now < confirmationUntil;
        if ((interactionPreview.Copy != null && interactionPreview.Copy.Key != dismissedCopyKey) || confirming)
        {
            if (copyHud == null)
            {
                copyHud = new InteractionHudWindow(false) { Owner = overlay };
                copyHud.CopyRequested += CopyNearbyMarker;
                copyHud.Dismissed += () => { dismissedCopyKey = interactionPreview.Copy?.Key; confirmationUntil = 0; };
            }
            var candidate = interactionPreview.Copy;
            string detail = candidate == null ? "" : "In range · " + System.Numerics.Vector3.Distance(candidate.Position, gameFrame.AvatarPosition).ToString("0.0", CultureInfo.InvariantCulture)
                + " m / " + candidate.CopyRadius.ToString("0.##", CultureInfo.InvariantCulture) + " m · Copies text only; does not teleport or send chat.";
            copyHud.SetText(confirming ? confirmationText : candidate?.Copy ?? "", detail,
                candidate != null && InteractionPreferences.MarkerClipboard != MarkerClipboardMode.Never);
            copyHud.Present(bounds, overlay.Handle, false);
        }
        else copyHud?.Hide();
    }

    private void CopyNearbyMarker()
    {
        if (!CanInteractInWorld() || interactionPreview.Copy is not { } marker || gameFrame == null) return;
        if (marker.MapId != gameFrame.MapId || !PreviewCategoryVisible(marker.Category)
            || System.Numerics.Vector3.DistanceSquared(marker.Position, gameFrame.AvatarPosition) > (double)marker.CopyRadius * marker.CopyRadius) return;
        TryMarkerCopy(marker.Copy, marker.Message);
    }

    private bool TryMarkerCopy(string payload, string message)
    {
        if (payload.Length == 0) { StatusText.Text = "This item has no Copy Text."; return false; }
        if (InteractionPreferences.MarkerClipboard == MarkerClipboardMode.Never)
        {
            StatusText.Text = "Clipboard actions are disabled in Preferences.";
            return false;
        }
        try
        {
            Clipboard.SetText(payload, TextDataFormat.UnicodeText);
            if (!Clipboard.ContainsText(TextDataFormat.UnicodeText) || Clipboard.GetText(TextDataFormat.UnicodeText) != payload)
                throw new ExternalException("Clipboard verification failed.");
            confirmationText = MarkerInteraction.Confirmation(message, payload);
            confirmationUntil = Environment.TickCount64 + 3000;
            StatusText.Text = confirmationText;
            overlay?.RecordingResult(confirmationText);
            return true;
        }
        catch (Exception ex) when (ex is ExternalException or System.Threading.ThreadStateException or InvalidOperationException or System.Security.SecurityException)
        {
            confirmationText = "Clipboard unavailable. Please try Copy text again.";
            confirmationUntil = Environment.TickCount64 + 3000;
            StatusText.Text = confirmationText;
            return false;
        }
    }

    private string InteractionBadge(XElement element)
    {
        if (element.Name.LocalName != "POI") return "";
        bool info = EffectiveValue(element, "info", "").Length > 0, copy = EffectiveValue(element, "copy", "").Length > 0;
        return info && copy ? " [Info + Copy]" : info ? " [Info]" : copy ? " [Copy]" : "";
    }

    private static string InteractionFieldLabel(string name) => name.ToLowerInvariant() switch
    {
        "info" => "Info Text", "inforange" => "Info Range (m)", "copy" => "Copy Text",
        "copy-message" => "Confirmation Message", "triggerrange" => "Trigger Range (m)", "autotrigger" => "Auto Trigger", _ => name
    };

    private string InteractionFieldDefault(XElement element, string name, string fallback)
    {
        if (!name.Equals("infoRange", StringComparison.OrdinalIgnoreCase)) return fallback;
        return (MarkerInteraction.TryRadius(EffectiveValue(element, "triggerRange", ""), out float range) ? range : 2)
            .ToString("R", CultureInfo.InvariantCulture);
    }

    private void AddInteractionActions(XElement element, string group)
    {
        if (group == "info")
        {
            AttributesHost.Children.Add(new TextBlock { Text = "Info Range shows/hides instructions independently of Copy. When omitted or invalid, it uses Trigger Range, then 2 meters.", TextWrapping = TextWrapping.Wrap });
            var preview = new Button { Content = "Preview Info · layout only", Margin = new Thickness(0, 6, 0, 6) };
            preview.Click += (_, _) =>
            {
                var window = new InteractionHudWindow(true, true) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                window.SetText(EffectiveValue(element, "info", ""), "Layout preview only · no proximity or clipboard test.");
                window.Show();
            };
            AttributesHost.Children.Add(preview);
        }
        if (group == "copy")
        {
            AttributesHost.Children.Add(new TextBlock { Text = element.Name.LocalName == "MarkerCategory"
                ? "Category menu actions use only Copy Text and Confirmation Message. Range and Auto Trigger affect inheriting markers."
                : "Trigger Range enables Copy. Automatic writes also require Test marker interactions in the overlay.", TextWrapping = TextWrapping.Wrap });
            var test = new Button { Content = "Test Copy", Margin = new Thickness(0, 6, 0, 6) };
            test.Click += (_, _) => TestElementCopy(element);
            AttributesHost.Children.Add(test);
            if (element.Name.LocalName == "POI")
            {
                interactionRangeStatus = new TextBlock { Text = "Open the overlay on a playable map to check range.", TextWrapping = TextWrapping.Wrap };
                AttributesHost.Children.Add(interactionRangeStatus);
            }
        }
    }

    private void TestElementCopy(XElement element)
    {
        string payload = element.Name.LocalName == "MarkerCategory" ? Value(element, "copy") : EffectiveValue(element, "copy", "");
        string message = element.Name.LocalName == "MarkerCategory" ? Value(element, "copy-message") : EffectiveValue(element, "copy-message", "");
        bool copied = TryMarkerCopy(payload, message);
        Notice(copied ? "Copy test succeeded" : "Copy test did not copy", "Exact decoded payload:\n" + payload + "\n\nConfirmation preview:\n"
            + MarkerInteraction.Confirmation(message, payload) + (copied ? "" : "\n\n" + StatusText.Text));
    }

    private void CopyOverlayCategory(string xml, string path)
    {
        var category = pages.FirstOrDefault(p => p.Name == xml)?.Xml.Descendants("MarkerCategory").FirstOrDefault(c => CategoryFullPath(c) == path);
        if (category != null) TryMarkerCopy(Value(category, "copy"), Value(category, "copy-message"));
    }
}
