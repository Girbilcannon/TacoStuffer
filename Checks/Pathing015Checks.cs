using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TacoStuffer.Models;
using TacoStuffer.Services;

internal static unsafe class Pathing015Checks
{
    private static uint boundStride, submittedIndices;
    private static Vector3 secondPosition;
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }

    public static void Run()
    {
        var labels = new Dictionary<string, string>
        {
            ["pack"] = "Adventures", ["pack.a"] = "Lake", ["pack.a.child"] = "Checkpoint",
            ["pack.a-sibling"] = "Hill", ["pack.empty"] = "", ["other"] = "Adventures"
        };
        var categories = PathChoices.Categories(labels, current: "external.route", includeNone: true);
        Check(categories[0].Value == "" && categories[0].Label == "None (no action)", "Explicit empty action remains selectable");
        var child = categories.Single(c => c.Value == "pack.a.child");
        Check(child.Label == "Checkpoint" && child.Depth == 2 && child.ToString() == "      Checkpoint", "Indented DisplayName instead of namespace/object serialization");
        Check(categories.FindIndex(c => c.Value == "pack.a.child") < categories.FindIndex(c => c.Value == "pack.a-sibling"), "Subtree stays together even with punctuation in sibling names");
        Check(categories.Single(c => c.Value == "pack.empty").Label == "empty"
            && categories.Single(c => c.Value == "external.route").Label == "route", "Missing DisplayName/external namespace fallback preserves IDs");
        Check(categories.Count(c => c.Label == "Adventures") == 2, "Duplicate display labels retain distinct namespace choices");
        var files = PathChoices.Files(["scripts/a/main.lua", "scripts/b/main.lua", "pack.lua"]);
        Check(files.Count(c => c.Label == "main.lua") == 2 && files.Single(c => c.Value == "scripts/a/main.lua").Depth == 2
            && files.Where(c => !c.IsSelectable).Select(c => c.Value).Order().SequenceEqual(new[] { "scripts", "scripts/a", "scripts/b" }), "File choices preserve distinct full paths with folder headings and indented filenames");

        CheckDrawBinding();
    }

    private static void CheckDrawBinding()
    {
        // Exercise the production Draw method against a minimal fake COM context.
        // No GPU or Windows DLL is invoked: these callbacks capture its ABI calls.
        nint table = Marshal.AllocHGlobal(20 * IntPtr.Size), context = Marshal.AllocHGlobal(IntPtr.Size);
        TrailGpuVertex* vertices = stackalloc TrailGpuVertex[2];
        vertices[0] = new(new(1, 2, 3), new(4, 5), new(6, 7, 8), 9, new(10, 11, 12));
        vertices[1] = new(new(101, 102, 103), new(14, 15), new(16, 17, 18), 19, new(20, 21, 22));
        try
        {
            for (int i = 0; i < 20; i++) ((nint*)table)[i] = 0;
            *(nint*)context = table;
            ((nint*)table)[18] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, uint*, uint*, void>)&BindVertices;
            ((nint*)table)[19] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, uint, uint, void>)&BindIndices;
            ((nint*)table)[8] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, void>)&BindTexture;
            ((nint*)table)[12] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, uint, int, void>)&DrawIndexed;
            Direct3D11Native.Draw(context, (nint)vertices, 0, 0, 6);
            Check(boundStride == Marshal.SizeOf<TrailGpuVertex>() && secondPosition == new Vector3(101, 102, 103),
                "Actual GPU draw binding must step through full vertices, not interpret wall-offset bytes as positions");
            Check(submittedIndices == 6, "Draw submits the expected index count");
        }
        finally { Marshal.FreeHGlobal(context); Marshal.FreeHGlobal(table); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void BindVertices(nint context, uint start, uint count, nint* vertices, uint* strides, uint* offsets)
    {
        boundStride = strides[0];
        secondPosition = *(Vector3*)((byte*)vertices[0] + offsets[0] + strides[0]);
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void BindIndices(nint context, nint indices, uint format, uint offset) { }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void BindTexture(nint context, uint start, uint count, nint* texture) { }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void DrawIndexed(nint context, uint count, uint start, int baseVertex) => submittedIndices = count;
}
