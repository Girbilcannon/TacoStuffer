using System.IO;
using System.IO.MemoryMappedFiles;
using TacoStuffer.Models;

namespace TacoStuffer.Services;

public sealed class MumbleLinkReader : IDisposable
{
    private MemoryMappedFile? mapping;
    private MemoryMappedViewAccessor? view;
    private readonly byte[] buffer = new byte[MumbleSnapshot.FrameSize];
    private MumbleSnapshot? cached;
    public string LinkName { get; }
    public string? Error { get; private set; }
    public MumbleLinkReader(string name = "MumbleLink") => LinkName = name;

    // Read-only shared-memory access: the game owns the producer. The tick checks
    // below reject changing snapshots instead of assuming the writer takes a lock.
    public MumbleSnapshot? Read()
    {
        try
        {
            if (mapping == null)
            {
                mapping = MemoryMappedFile.OpenExisting(LinkName, MemoryMappedFileRights.Read);
                view = mapping.CreateViewAccessor(0, MumbleSnapshot.FrameSize, MemoryMappedFileAccess.Read);
            }
            Error = null;
            // The writer does not lock. Retry a changing frame; never publish
            // a copy whose tick changed while it was being read.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                uint before = view!.ReadUInt32(4);
                if (cached != null && cached.Tick == before) return cached;
                view.ReadArray(0, buffer, 0, buffer.Length);
                uint after = view.ReadUInt32(4);
                if (before == after && BitConverter.ToUInt32(buffer, 4) == after) return cached = MumbleSnapshot.Decode(buffer);
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Error = ex is FileNotFoundException ? "Waiting for MumbleLink" : "MumbleLink: " + ex.Message;
            Dispose();
            return null;
        }
    }

    public void Dispose()
    {
        cached = null;
        view?.Dispose();
        view = null;
        mapping?.Dispose();
        mapping = null;
    }
}
