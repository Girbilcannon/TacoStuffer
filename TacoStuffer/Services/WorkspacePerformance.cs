using System.Diagnostics;
using System.IO;

namespace TacoStuffer.Services;

// Bounded, best-effort diagnostics for real Windows workloads. Logging never
// performs disk IO on the dispatcher and never changes editing behavior.
public static class WorkspacePerformance
{
    private static readonly object LogLock = new();
    public static IDisposable Measure(string operation) => new Timing(operation);

    private sealed class Timing(string operation) : IDisposable
    {
        private readonly long started = Stopwatch.GetTimestamp();
        private readonly long allocated = GC.GetAllocatedBytesForCurrentThread();
        private readonly int thread = Environment.CurrentManagedThreadId;
        public void Dispose()
        {
            double milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (milliseconds < 100) return;
            long bytes = thread == Environment.CurrentManagedThreadId ? GC.GetAllocatedBytesForCurrentThread() - allocated : -1;
            string line = $"{DateTime.UtcNow:O} {operation}: {milliseconds:F1} ms; thread {thread}; allocated {(bytes < 0 ? "n/a" : (bytes / 1024).ToString() + " KB")}\n";
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    lock (LogLock)
                    {
                        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TacoStuffer");
                        Directory.CreateDirectory(folder);
                        string file = Path.Combine(folder, "performance.log");
                        if (File.Exists(file) && new FileInfo(file).Length > 1024 * 1024) File.Move(file, file + ".previous", true);
                        File.AppendAllText(file, line);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            });
        }
    }
}
