using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace TacoStuffer;

public partial class MainWindow
{
    // Retain the process object/handle, not just a PID that Windows can reuse.
    private Process? gameSessionProcess;
    private bool gameCloseRequested, forceCloseInProgress, applicationClosePending;
    private string? approvedCloseFingerprint;
    private static bool SessionExited(Process? process)
    {
        if (process == null) return true;
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private void TrackGameProcess(Process process)
    {
        try
        {
            _ = process.Handle;
        } // Open and retain the exact native process identity now.
        catch
        {
            process.Dispose();
            throw;
        }
        if (gameSessionProcess != null && !SessionExited(gameSessionProcess) && gameSessionProcess.Id == process.Id)
        {
            process.Dispose();
            return;
        }
        gameSessionProcess?.Dispose();
        gameSessionProcess = process;
    }

    // Only forget hosting after the retained process has exited. Restoring a dead
    // HWND as if this were a desktop detach can produce invalid-handle errors.
    private void CompleteGameExit(bool keepGamePane = false)
    {
        // This path is only called after the retained process reports exit.
        // A dead client needs no SetParent/style/owner restoration.
        if (!SessionExited(gameSessionProcess)) return;
        GameViewport.ForgetExitedClient();
        waitingToHostGame = mountingGameViewport = wasHostingGame = gameCloseRequested = false;
        gameAttachRevision++;
        gameHostWarning = null;
        overlay?.Close();
        StopWorldRendering();
        HideGameViewport();
        if (!keepGamePane) ExitInAppLayout();
        else
        {
            hostedCodeWindow?.Hide();
            ReleaseHostedEditor();
        }
        gameSessionProcess?.Dispose();
        gameSessionProcess = null;
        ForceCloseGameButton.IsEnabled = false;
        UpdateGameLayoutControls();
    }

    private async Task<bool> WaitForGameExit(Process process)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return SessionExited(process);
        }
        catch (OperationCanceledException)
        {
            return SessionExited(process);
        }
        catch (InvalidOperationException)
        {
            return SessionExited(process);
        }
    }

    // The red emergency button targets the retained game session process. It must
    // not discover and terminate unrelated GW2 instances by executable name.
    private async void ForceCloseHostedGame(object sender, RoutedEventArgs e)
    {
        var process = gameSessionProcess;
        if (forceCloseInProgress || process == null) return;
        if (SessionExited(process))
        {
            CompleteGameExit(keepGamePane: true);
            return;
        }
        forceCloseInProgress = true;
        ForceCloseGameButton.IsEnabled = false;
        gameCloseRequested = true;
        waitingToHostGame = mountingGameViewport = false;
        gameAttachRevision++;
        GameViewport.SuspendHosting();
        StopTrailRecording();
        try
        {
            // Explicit red button: terminate only this launched/hosted GW2
            // process. Do not kill Taco Stuffer, other clients or a process tree.
            process.Kill(entireProcessTree: false);
            ReportGameHost("Force closing this GW2 session…");
            if (await WaitForGameExit(process))
            {
                CompleteGameExit(keepGamePane: true);
                ReportGameHost("GW2 closed. Your project remains open; Launch Windowed can start a new session.");
            }
            else ReportGameHost("Waiting for GW2 to exit. Force Close Game can retry if necessary.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            if (SessionExited(process))
            {
                CompleteGameExit(keepGamePane: true);
                ReportGameHost("GW2 closed.");
            }
            else
            {
                gameCloseRequested = false;
                GameViewport.ResumeHosting();
                ReportGameHost("Could not force close GW2: " + ex.Message);
            }
        }
        finally
        {
            forceCloseInProgress = false;
            ForceCloseGameButton.IsEnabled = !SessionExited(gameSessionProcess);
            UpdateGameLayoutControls();
        }
    }

    private async void EditorClosing(object? sender, CancelEventArgs e)
    {
        if (workspaceBusy)
        {
            e.Cancel = true;
            StatusText.Text = "Wait for the current import or trail conversion to finish before closing Taco Stuffer.";
            return;
        }
        if (applicationClosePending || forceCloseInProgress)
        {
            e.Cancel = true;
            return;
        }
        if (approvedCloseFingerprint != WorkspaceFingerprint() && !AllowProjectChange())
        {
            e.Cancel = true;
            return;
        }
        var process = gameSessionProcess;
        if (inAppLayout && !SessionExited(process))
        {
            e.Cancel = true;
            applicationClosePending = true;
            StopTrailRecording();
            approvedCloseFingerprint = WorkspaceFingerprint();
            gameCloseRequested = true;
            waitingToHostGame = mountingGameViewport = false;
            gameAttachRevision++;
            GameViewport.SuspendHosting();
            try
            {
                var client = GameViewport.Client;
                bool requested = client.Handle != 0 ? PostMessage(client.Handle, 0x0010, 0, 0) : process!.CloseMainWindow(); // WM_CLOSE
                ReportGameHost(requested ? "Closing GW2 before exiting Taco Stuffer…" : "GW2 did not accept the close request. Use Force Close Game if it is stuck.");
                if (await WaitForGameExit(process!))
                {
                    CompleteGameExit();
                    applicationClosePending = false;
                    Close();
                }
                else ReportGameHost("GW2 has not exited. Finish closing it, or use Force Close Game. Your project remains open.");
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                if (SessionExited(process))
                {
                    CompleteGameExit();
                    applicationClosePending = false;
                    Close();
                }
                else ReportGameHost("Could not close GW2: " + ex.Message + ". Use Force Close Game if needed.");
            }
            finally
            {
                applicationClosePending = false;
                ForceCloseGameButton.IsEnabled = !SessionExited(gameSessionProcess);
                UpdateGameLayoutControls();
            }
            return;
        }
        if (process != null && SessionExited(process)) CompleteGameExit();
        if (!ReturnGameToDesktop()) e.Cancel = true;
    }

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    private static extern bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
}
