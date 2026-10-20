using System.Runtime.InteropServices;
using System.Text;
namespace TacoStuffer.Services;

// Narrow Windows SDK ABI surface. No third-party graphics runtime, game hooks
// or GPU-to-CPU readback. All calls run on the WPF dispatcher thread.
// Native COM interop: vtable slots, calling conventions and struct layouts are
// part of the Direct3D ABI. Changes here need Windows rendering tests; matching
// C# names alone does not establish that an interop declaration is correct.
internal static unsafe class Direct3D11Native
{
    internal static readonly Guid DxgiDeviceId = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    internal static readonly Guid Factory2Id = new("50c83a1c-e072-4c48-87b0-3630fa36a6d0");
    internal static readonly Guid Texture2DId = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    internal static readonly Guid CompositionDeviceId = new("c37ea93a-e7aa-450d-b16f-9746cb0407f3");
    private static void* Slot(nint obj, int index) => ((void**)*(void**)obj)[index];
    internal static void ValidateAbi()
    {
        if (sizeof(SwapDescription) != 48 || sizeof(TextureDescription) != 44 || sizeof(BufferDescription) != 24 ||
            sizeof(BlendDescription) != 264 || sizeof(TargetBlend) != 32 || sizeof(RasterDescription) != 40 || sizeof(SamplerDescription) != 52 ||
            sizeof(InputElement) != (IntPtr.Size == 8 ? 32 : 28) || sizeof(InitialData) != (IntPtr.Size == 8 ? 16 : 12))
            throw new NotSupportedException("Direct3D structure layout does not match the Windows SDK ABI.");
    }
    internal static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    internal static void Release(ref nint obj)
    {
        nint value = obj; obj = 0;
        if (value != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(value, 2))(value);
    }
    internal static nint Query(nint obj, Guid id)
    {
        nint result = 0; Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(obj, 0))(obj, &id, &result)); return result;
    }
    [DllImport("d3d11.dll", ExactSpelling = true)]
    internal static extern int D3D11CreateDevice(nint adapter, uint driverType, nint software, uint flags, nint levels, uint levelCount, uint sdkVersion, out nint device, out uint featureLevel, out nint context);
    [DllImport("dcomp.dll", ExactSpelling = true)]
    internal static extern int DCompositionCreateDevice(nint device, in Guid id, out nint composition);
    [DllImport("d3dcompiler_47.dll", ExactSpelling = true, CharSet = CharSet.Ansi)]
    private static extern int D3DCompile(byte[] data, nuint size, string? name, nint defines, nint include, string entry, string target, uint flags, uint flags2, out nint code, out nint errors);
    internal static nint Compile(string shader, string entry, string target)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(shader);
        int hr = D3DCompile(bytes, (nuint)bytes.Length, "TacoStufferTrail", 0, 0, entry, target, 1 << 15, 0, out nint code, out nint errors);
        try
        {
            if (hr < 0)
            {
                string message = errors == 0 ? "Trail shader compilation failed." : Marshal.PtrToStringAnsi(BlobData(errors)) ?? "Trail shader compilation failed.";
                Release(ref code); throw new InvalidOperationException(message, Marshal.GetExceptionForHR(hr));
            }
            return code;
        }
        finally { Release(ref errors); }
    }
    internal static nint BlobData(nint blob) => ((delegate* unmanaged[Stdcall]<nint, nint>)Slot(blob, 3))(blob);
    internal static nuint BlobSize(nint blob) => ((delegate* unmanaged[Stdcall]<nint, nuint>)Slot(blob, 4))(blob);
    internal static nint Adapter(nint dxgiDevice)
    {
        nint value = 0; Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(dxgiDevice, 7))(dxgiDevice, &value)); return value;
    }
    internal static nint Factory(nint adapter)
    {
        nint value = 0; Guid id = Factory2Id; Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(adapter, 6))(adapter, &id, &value)); return value;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct SwapDescription
    {
        public uint Width, Height, Format, Stereo, SampleCount, SampleQuality, Usage, BufferCount, Scaling, SwapEffect, AlphaMode, Flags;
    }
    internal static nint SwapChain(nint factory, nint device, uint width, uint height)
    {
        var desc = new SwapDescription { Width = width, Height = height, Format = 87, SampleCount = 1, Usage = 0x20, BufferCount = 2, SwapEffect = 3, AlphaMode = 1 };
        nint value = 0; Check(((delegate* unmanaged[Stdcall]<nint, nint, SwapDescription*, nint, nint*, int>)Slot(factory, 24))(factory, device, &desc, 0, &value)); return value;
    }
    internal static nint BackBuffer(nint swap)
    {
        nint value = 0; Guid id = Texture2DId; Check(((delegate* unmanaged[Stdcall]<nint, uint, Guid*, nint*, int>)Slot(swap, 9))(swap, 0, &id, &value)); return value;
    }
    internal static void Resize(nint swap, uint width, uint height) => Check(((delegate* unmanaged[Stdcall]<nint, uint, uint, uint, uint, uint, int>)Slot(swap, 13))(swap, 2, width, height, 87, 0));
    // Nonblocking present avoids stalling the editor behind a busy GPU. A full
    // queue drops this frame; the next composition tick renders current data.
    internal static bool Present(nint swap, out bool occluded)
    {
        int hr = ((delegate* unmanaged[Stdcall]<nint, uint, uint, int>)Slot(swap, 8))(swap, 0, 8);
        occluded = hr == 0x087A0001;
        if (hr == unchecked((int)0x887A000A)) return false; // DXGI_ERROR_WAS_STILL_DRAWING
        if (occluded) return false; // Retry when visible; this is not a displayed frame
        Check(hr); return true;
    }
    internal static nint CompositionTarget(nint composition, nint hwnd)
    {
        nint value = 0; Check(((delegate* unmanaged[Stdcall]<nint, nint, int, nint*, int>)Slot(composition, 6))(composition, hwnd, 1, &value)); return value;
    }
    internal static nint CompositionVisual(nint composition)
    {
        nint value = 0; Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(composition, 7))(composition, &value)); return value;
    }
    internal static void SetContent(nint visual, nint content) => Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(visual, 15))(visual, content));
    internal static void SetRoot(nint target, nint visual) => Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(target, 3))(target, visual));
    internal static void Commit(nint composition) => Check(((delegate* unmanaged[Stdcall]<nint, int>)Slot(composition, 3))(composition));

    [StructLayout(LayoutKind.Sequential)] internal struct BufferDescription { public uint Bytes, Usage, Bind, CpuAccess, Misc, StructureStride; }
    [StructLayout(LayoutKind.Sequential)] internal struct InitialData { public nint Data; public uint RowPitch, SlicePitch; }
    internal static nint Buffer(nint device, uint bytes, uint bind, void* data = null)
    {
        var desc = new BufferDescription { Bytes = bytes, Bind = bind, Usage = data == null ? 0u : 1u };
        var initial = new InitialData { Data = (nint)data }; nint value = 0;
        Check(((delegate* unmanaged[Stdcall]<nint, BufferDescription*, InitialData*, nint*, int>)Slot(device, 3))(device, &desc, data == null ? null : &initial, &value)); return value;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct TextureDescription { public uint Width, Height, Mips, ArraySize, Format, SampleCount, SampleQuality, Usage, Bind, CpuAccess, Misc; }
    internal static nint Texture(nint device, uint width, uint height, byte* pixels)
    {
        var desc = new TextureDescription { Width = width, Height = height, Mips = 1, ArraySize = 1, Format = 87, SampleCount = 1, Usage = 1, Bind = 8 };
        var initial = new InitialData { Data = (nint)pixels, RowPitch = checked(width * 4) }; nint value = 0;
        Check(((delegate* unmanaged[Stdcall]<nint, TextureDescription*, InitialData*, nint*, int>)Slot(device, 5))(device, &desc, &initial, &value)); return value;
    }
    internal static nint ResourceView(nint device, nint resource, bool renderTarget)
    {
        nint value = 0; Check(((delegate* unmanaged[Stdcall]<nint, nint, void*, nint*, int>)Slot(device, renderTarget ? 9 : 7))(device, resource, null, &value)); return value;
    }
    internal static nint Shader(nint device, nint blob, bool vertex)
    {
        nint value = 0; Check(((delegate* unmanaged[Stdcall]<nint, nint, nuint, nint, nint*, int>)Slot(device, vertex ? 12 : 15))(device, BlobData(blob), BlobSize(blob), 0, &value)); return value;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct InputElement { public nint Name; public uint Index, Format, Slot, Offset, Classification, Step; }
    internal static nint Layout(nint device, nint blob)
    {
        nint position = Marshal.StringToHGlobalAnsi("POSITION"), uv = Marshal.StringToHGlobalAnsi("TEXCOORD");
        try
        {
            InputElement* elements = stackalloc InputElement[4];
            elements[0] = new() { Name = position, Format = 6 };
            elements[1] = new() { Name = uv, Format = 16, Offset = 12 };
            elements[2] = new() { Name = uv, Index = 1, Format = 6, Offset = 20 };
            elements[3] = new() { Name = uv, Index = 2, Format = 41, Offset = 32 };
            nint value = 0; Check(((delegate* unmanaged[Stdcall]<nint, InputElement*, uint, nint, nuint, nint*, int>)Slot(device, 11))(device, elements, 4, BlobData(blob), BlobSize(blob), &value)); return value;
        }
        finally { Marshal.FreeHGlobal(position); Marshal.FreeHGlobal(uv); }
    }
    [StructLayout(LayoutKind.Sequential)] internal struct TargetBlend { public uint Enabled, Source, Destination, Operation, SourceAlpha, DestinationAlpha, AlphaOperation; public byte Mask; }
    [StructLayout(LayoutKind.Sequential)] internal struct BlendDescription { public uint Coverage, Independent; public TargetBlend T0, T1, T2, T3, T4, T5, T6, T7; }
    internal static nint Blend(nint device)
    {
        var desc = new BlendDescription { T0 = new() { Enabled = 1, Source = 2, Destination = 6, Operation = 1, SourceAlpha = 2, DestinationAlpha = 6, AlphaOperation = 1, Mask = 15 } }; nint value = 0;
        Check(((delegate* unmanaged[Stdcall]<nint, BlendDescription*, nint*, int>)Slot(device, 20))(device, &desc, &value)); return value;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct RasterDescription { public uint Fill, Cull, CounterClockwise; public int DepthBias; public float BiasClamp, SlopeBias; public uint DepthClip, Scissor, Multisample, Antialias; }
    internal static nint Raster(nint device)
    {
        var desc = new RasterDescription { Fill = 3, Cull = 1, DepthClip = 1 }; nint value = 0;
        Check(((delegate* unmanaged[Stdcall]<nint, RasterDescription*, nint*, int>)Slot(device, 22))(device, &desc, &value)); return value;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct SamplerDescription { public uint Filter, U, V, W; public float Bias; public uint Anisotropy, Comparison; public float R, G, B, A, MinLod, MaxLod; }
    internal static nint Sampler(nint device)
    {
        var desc = new SamplerDescription { Filter = 0x15, U = 3, V = 1, W = 1, Anisotropy = 1, Comparison = 8, MaxLod = float.MaxValue }; nint value = 0;
        Check(((delegate* unmanaged[Stdcall]<nint, SamplerDescription*, nint*, int>)Slot(device, 23))(device, &desc, &value)); return value;
    }
    internal static void Update(nint context, nint buffer, void* data) => ((delegate* unmanaged[Stdcall]<nint, nint, uint, void*, void*, uint, uint, void>)Slot(context, 48))(context, buffer, 0, null, data, 0, 0);
    [StructLayout(LayoutKind.Sequential)] internal struct Box { public uint Left, Top, Front, Right, Bottom, Back; }
    internal static void UpdateRange(nint context, nint buffer, uint offset, uint bytes, void* data)
    {
        var box = new Box { Left = offset, Right = checked(offset + bytes), Bottom = 1, Back = 1 };
        ((delegate* unmanaged[Stdcall]<nint, nint, uint, Box*, void*, uint, uint, void>)Slot(context, 48))(context, buffer, 0, &box, data, 0, 0);
    }
    internal static void Targets(nint context, nint target) => ((delegate* unmanaged[Stdcall]<nint, uint, nint*, nint, void>)Slot(context, 33))(context, target == 0 ? 0u : 1u, target == 0 ? null : &target, 0);
    internal static void Clear(nint context, nint target)
    {
        float* color = stackalloc float[4]; color[0] = color[1] = color[2] = color[3] = 0;
        ((delegate* unmanaged[Stdcall]<nint, nint, float*, void>)Slot(context, 50))(context, target, color);
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Viewport { public float X, Y, Width, Height, MinDepth, MaxDepth; }
    internal static void Pipeline(nint context, nint vertex, nint pixel, nint layout, nint constants, nint blend, nint raster, nint sampler, uint width, uint height)
    {
        ((delegate* unmanaged[Stdcall]<nint, nint, void>)Slot(context, 17))(context, layout);
        ((delegate* unmanaged[Stdcall]<nint, uint, void>)Slot(context, 24))(context, 4); // triangle list
        ((delegate* unmanaged[Stdcall]<nint, nint, nint*, uint, void>)Slot(context, 11))(context, vertex, null, 0);
        ((delegate* unmanaged[Stdcall]<nint, nint, nint*, uint, void>)Slot(context, 9))(context, pixel, null, 0);
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, void>)Slot(context, 7))(context, 0, 1, &constants);
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, void>)Slot(context, 16))(context, 0, 1, &constants);
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, void>)Slot(context, 10))(context, 0, 1, &sampler);
        ((delegate* unmanaged[Stdcall]<nint, nint, float*, uint, void>)Slot(context, 35))(context, blend, null, uint.MaxValue);
        ((delegate* unmanaged[Stdcall]<nint, nint, void>)Slot(context, 43))(context, raster);
        var view = new Viewport { Width = width, Height = height, MaxDepth = 1 };
        ((delegate* unmanaged[Stdcall]<nint, uint, Viewport*, void>)Slot(context, 44))(context, 1, &view);
    }
    internal static void Draw(nint context, nint vertices, nint indices, nint texture, uint count)
    {
        uint stride = 36, offset = 0;
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, uint*, uint*, void>)Slot(context, 18))(context, 0, 1, &vertices, &stride, &offset);
        ((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, void>)Slot(context, 19))(context, indices, 42, 0); // R32_UINT
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, void>)Slot(context, 8))(context, 0, 1, &texture);
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, int, void>)Slot(context, 12))(context, count, 0, 0);
    }
    internal static void ClearState(nint context) => ((delegate* unmanaged[Stdcall]<nint, void>)Slot(context, 110))(context);
}
