using System.IO;

namespace TacoStuffer.Models;

public sealed record PackDeliveryResult(IReadOnlyList<string> Written, IReadOnlyList<string> Errors);

public static class PackDelivery
{
    public static PackDeliveryResult Copy(string compiledPack,string filename,IEnumerable<string> folders)
    {
        if (!ProjectStore.ValidFileName(filename)) throw new InvalidOperationException("Invalid pack filename.");
        var written = new List<string>(); var errors = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in folders.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            string? temp = null;
            try
            {
                string normalized = folder.Trim();
                if (!Path.IsPathFullyQualified(normalized)) throw new InvalidOperationException("Choose an absolute folder path.");
                string target = Path.Combine(Path.GetFullPath(normalized),filename);
                if (!visited.Add(target)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.Copy(compiledPack,temp,false); File.Move(temp,target,true); temp = null;
                written.Add(target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
            { errors.Add(folder + ": " + ex.Message); }
            finally { if (temp != null && File.Exists(temp)) try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
        return new(written,errors);
    }
}
