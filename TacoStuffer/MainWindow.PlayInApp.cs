using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using TacoStuffer.Services;
using TacoStuffer.Views;

namespace TacoStuffer;

public partial class MainWindow
{
    // Constructing the managed object is safe. Insert it into the visual tree
    // only for an explicit attachment, after the Game tab has a layout.
    private readonly GameViewportHost GameViewport = new() { Visibility = Visibility.Collapsed };
    private bool waitingToHostGame;
    private bool wasHostingGame;
    private bool mountingGameViewport;
    private int gameAttachRevision;
    private long hostLaunchTime, hostCandidateSince;
    private GameClientWindow hostCandidate;
    private (int Width, int Height, nint Dpi)? hostCandidateGeometry;
    private long hostGeometrySince;
    private string? gameHostWarning;
    private string gameHostDetails = "No attachment attempted.";
    private static readonly object GameHostLogLock = new();
    private void InitializePlayInApp()
    {
        LocationChanged += (_, _) => UpdateHostedCodeVisibility();
        GameViewportSlot.SizeChanged += (_, _) => UpdateHostedCodeVisibility();
        UpdateGameLayoutControls();
        Closed += (_, _) =>
        {
            ReleaseHostedEditor();
            hostedCodeWindow?.Close();
            GameViewport.Dispose();
            gameSessionProcess?.Dispose();
        };
    }

    private void ReportGameHost(string message, string details = "")
    {
        GameViewportStatus.Text = message;
        gameHostDetails = details.Length > 0 ? details : message;
        string line = $"{DateTime.UtcNow:O} {message}\n{gameHostDetails}\n";
        _ = Task.Run(() =>
        {
            try
            {
                lock (GameHostLogLock)
                {
                    string folder = Path.GetDirectoryName(PreferenceFile)!;
                    Directory.CreateDirectory(folder);
                    string file = Path.Combine(folder, "game-hosting.log");
                    if (File.Exists(file) && new FileInfo(file).Length > 1024 * 1024) File.WriteAllText(file, "");
                    File.AppendAllText(file, line);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        });
    }

    private void LaunchHostedGame(object sender, RoutedEventArgs e)
    {
        if (gameCloseRequested || forceCloseInProgress || applicationClosePending)
        {
            ReportGameHost("Wait for this GW2 session to finish closing before launching again.");
            return;
        }
        if (GameViewport.IsAttached)
        {
            ReportGameHost("GW2 is already in the Game tab. Click inside the game to play.");
            return;
        }
        if (GameClientWindow.Find(0).ProcessId != 0)
        {
            ReportGameHost("GW2 is already running. Close the existing client before using Launch Windowed for this test build.");
            return;
        }
        if (string.IsNullOrWhiteSpace(preferences.GameExecutablePath)) ShowGamePreferences(sender, e);
        string path = preferences.GameExecutablePath;
        if (!File.Exists(path))
        {
            ReportGameHost("Set the Guild Wars 2 executable path in Edit → Preferences → Game.");
            return;
        }
        try
        {
            var start = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! };
            start.ArgumentList.Add("-windowed");
            var process = Process.Start(start);
            if (process == null) throw new InvalidOperationException("GW2 did not start.");
            TrackGameProcess(process);
            ForceCloseGameButton.IsEnabled = true;
            waitingToHostGame = true;
            hostLaunchTime = Environment.TickCount64;
            hostCandidate = default;
            hostCandidateGeometry = null;
            UpdateGameLayoutControls();
            ReportGameHost("Launcher opened separately. Click Play; the GW2 render window will attach here automatically.", "Launched PID " + process.Id + " with -windowed.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            waitingToHostGame = false;
            ReportGameHost("Launch failed: " + ex.Message);
        }
    }

    private void AttachHostedGame(object sender, RoutedEventArgs e)
    {
        bool requestFocus = waitingToHostGame == false;
        waitingToHostGame = false;
        if (mountingGameViewport) return;
        if (GameViewport.IsAttached)
        {
            GameViewport.FocusGame();
            return;
        }
        // Hosting works at character selection too; recording remains gated by
        // fresh playable-map MumbleLink telemetry in the existing overlay code.
        var client = GameClientWindow.FindRenderClient(mumbleSession.IsLive ? gameFrame?.ProcessId ?? 0 : 0);
        if (client.Handle == 0)
        {
            ReportGameHost("No GW2 render window found. Click Play in the launcher and wait for the game window.",
                $"Detected PID {gameClient.ProcessId}; Mumble playable={mumbleSession.IsPlayable}; Mumble PID={gameFrame?.ProcessId ?? 0}.");
            return;
        }
        try
        {
            var process = Process.GetProcessById(client.ProcessId);
            if (process.HasExited)
            {
                process.Dispose();
                ReportGameHost("The GW2 window closed before attachment.");
                return;
            }
            TrackGameProcess(process);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            ReportGameHost("Could not track the GW2 session: " + ex.Message);
            return;
        }
        if (!GameTab.IsSelected) OpenTab(GameTab);
        mountingGameViewport = true;
        int request = ++gameAttachRevision;
        try
        {
            GameViewportPlaceholder.Visibility = Visibility.Collapsed;
            GameViewport.Visibility = Visibility.Visible;
            if (!GameViewportSlot.Children.Contains(GameViewport)) GameViewportSlot.Children.Add(GameViewport);
            GameViewportSlot.UpdateLayout();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            mountingGameViewport = false;
            HideGameViewport();
            ReportGameHost("Could not create the game viewport: " + ex.Message);
            return;
        }
        // Loaded priority lets HwndHost create and size its own HWND before SetParent.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (request != gameAttachRevision) return;
            mountingGameViewport = false;
            if (gameMonitorClosed || !GameTab.IsSelected || !GameViewportSlot.IsVisible)
            {
                HideGameViewport();
                return;
            }
            if (GameViewport.Attach(client))
            {
                wasHostingGame = true;
                gameHostWarning = null;
                if (showHostedXml) MountHostedEditor();
                ReportGameHost("GW2 attached · Click inside to play. Recording controls become available on a playable map.", GameViewport.DescribeClient(client));
                if (requestFocus) GameViewport.FocusGame();
                UpdateOverlayVisibility();
            }
            else
            {
                string details = GameViewport.DescribeClient(client);
                string error = GameViewport.LastError ?? "Could not attach GW2.";
                if (!GameViewport.IsAttached) HideGameViewport();
                ReportGameHost(error, details);
                Notice("Game attachment failed", error + "\n\n" + details);
            }
        }));
    }

    private void HideGameViewport()
    {
        GameViewport.HideContainer();
        GameViewport.Visibility = Visibility.Collapsed;
        GameViewportSlot.Children.Remove(GameViewport);
        GameViewportPlaceholder.Visibility = Visibility.Visible;
    }

    private void PollPlayInApp(bool ready)
    {
        UpdateGameLayoutControls();
        // Some launcher versions hand off to a new render process. Adopt the
        // live render process before interpreting the launcher's exit as quit.
        if (waitingToHostGame && !wasHostingGame)
        {
            var running = GameClientWindow.FindRenderClient(mumbleSession.IsLive ? gameFrame?.ProcessId ?? 0 : 0);
            if (running.ProcessId != 0 && (gameSessionProcess == null || SessionExited(gameSessionProcess) || gameSessionProcess.Id != running.ProcessId))
            {
                try
                {
                    TrackGameProcess(Process.GetProcessById(running.ProcessId));
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
                {
                }
            }
            if (SessionExited(gameSessionProcess) && running.ProcessId == 0 && Environment.TickCount64 - hostLaunchTime < 15000) return;
        }
        ForceCloseGameButton.IsEnabled = !forceCloseInProgress && !SessionExited(gameSessionProcess);
        if (gameSessionProcess != null && SessionExited(gameSessionProcess))
        {
            bool keepPane = gameCloseRequested && !applicationClosePending;
            CompleteGameExit(keepPane);
            ReportGameHost("GW2 closed. Your project remains open.");
            return;
        }
        if (gameCloseRequested)
        {
            UpdateHostedCodeVisibility();
            return;
        }
        if (GameViewport.IsAttached)
        {
            GameViewport.MaintainFrame();
            if (GameViewport.LastError is { } warning && warning != gameHostWarning)
            {
                gameHostWarning = warning;
                ReportGameHost(warning, GameViewport.DescribeClient(GameViewport.Client));
            }
        }
        if (wasHostingGame && !GameViewport.IsAttached)
        {
            gameCloseRequested = true;
            GameViewport.SuspendHosting();
            StopTrailRecording();
            ReportGameHost("The GW2 window closed or changed. Waiting for the session to exit; Force Close Game is available if it is stuck.");
            return;
        }
        UpdateHostedCodeVisibility();
        if (!waitingToHostGame || mountingGameViewport || !GameTab.IsSelected || !GameViewportSlot.IsVisible) return;
        var candidate = GameClientWindow.FindRenderClient(mumbleSession.IsLive ? gameFrame?.ProcessId ?? 0 : 0);
        long now = Environment.TickCount64;
        if (candidate != hostCandidate)
        {
            hostCandidate = candidate;
            hostCandidateSince = now;
            hostCandidateGeometry = null;
            if (candidate.Handle != 0) ReportGameHost("GW2 render window found. Waiting for its initial size and DPI to settle before attachment…");
        }
        bool settled = false;
        if (candidate.TryBounds(out var bounds) && !NativeGameWindow.IsHungAppWindow(candidate.Handle))
        {
            var geometry = (bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, NativeGameWindow.GetWindowDpiAwarenessContext(candidate.Handle));
            if (hostCandidateGeometry != geometry)
            {
                hostCandidateGeometry = geometry;
                hostGeometrySince = now;
            }
            settled = geometry.Item1 >= 128 && geometry.Item2 >= 128 && now - hostGeometrySince >= 2000;
        }
        else
        {
            hostCandidateGeometry = null;
            hostGeometrySince = now;
        }
        // A class/handle existing for two seconds did not guarantee startup
        // had settled. This is a conservative gate, not proof of GPU readiness.
        if (candidate.Handle != 0 && settled && now - hostCandidateSince >= 6000)
        {
            AttachHostedGame(this, new RoutedEventArgs());
            return;
        }
        if (gameClient.ProcessId == 0 && now - hostLaunchTime > 15000)
        {
            waitingToHostGame = false;
            ReportGameHost("No running GW2 client found. Use Launch Windowed to try again.");
        }
    }

    private void ReleaseHostedGame(object sender, RoutedEventArgs e) => ReturnGameToDesktop();

    private bool ReturnGameToDesktop()
    {
        if (gameSessionProcess != null && SessionExited(gameSessionProcess))
        {
            CompleteGameExit();
            return true;
        }
        if (gameCloseRequested)
        {
            ReportGameHost("GW2 is closing. Wait for it to exit, or use Force Close Game.");
            return false;
        }
        waitingToHostGame = false;
        mountingGameViewport = false;
        gameAttachRevision++;
        if (!GameViewport.Detach())
        {
            ReportGameHost(GameViewport.LastError ?? "Could not release the game window.", GameViewport.DescribeClient(GameViewport.Client));
            Notice("Game window", GameViewportStatus.Text);
            return false;
        }
        HideGameViewport();
        ExitInAppLayout();
        if (wasHostingGame)
        {
            ReportGameHost("GW2 returned to its own window. Change display mode in the game's graphics settings if needed.");
            wasHostingGame = false;
        }
        return true;
    }
}
