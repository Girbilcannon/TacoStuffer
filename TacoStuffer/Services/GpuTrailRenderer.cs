using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TacoStuffer.Models;
using TacoStuffer.Views;
using D3D = TacoStuffer.Services.Direct3D11Native;

namespace TacoStuffer.Services;

// Transparent, click-through native surface directly below the WPF selection
// surface. WPF never copies this GPU image into a layered window bitmap.
internal sealed unsafe class GpuTrailRenderer : IDisposable
{
    private nint window, device, context, swap, composition, target, visual, renderTarget;
    private nint vertexShader, pixelShader, layout, constants, blend, raster, sampler;
    private uint width = 1, height = 1;
    private NativeGameWindow.Rect bounds;
    private nint previousAbove;
    private bool shown, disposed, dirty = true, checkTextures = true;
    private string? projectRoot;
    private readonly Dictionary<TrailSegment, Mesh> meshes = [];
    private readonly Dictionary<string, TextureAsset> textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<TrailSegment> activeSegments = [];
    private readonly HashSet<TrailSegmentRef> previewSegments = [];
    private long lastTiming;
    private readonly long animationStart = Stopwatch.GetTimestamp();
    internal nint Handle => window;
    public void Invalidate() => dirty = true;

    private bool presentationRepair;
    // A stacking/visibility repair reuses existing meshes and textures. Keep this
    // separate from scene invalidation so ordinary editor clicks stay inexpensive.
    public void RequestPresentationRepair() => presentationRepair = true;

    public void SceneChanged()
    {
        dirty = true;
        checkTextures = true;
    }

    public void SetScene(IReadOnlyList<WorldTrail> trails)
    {
        SceneChanged();
        var live = trails.SelectMany(t => t.Trail.Segments).ToHashSet();
        foreach (var segment in meshes.Keys.Where(s => !live.Contains(s)).ToArray())
        {
            meshes[segment].Dispose();
            meshes.Remove(segment);
        }
        var paths = trails.Select(t => t.Texture).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in textures.Keys.Where(k => !paths.Contains(k)).ToArray())
        {
            textures[key].Dispose();
            textures.Remove(key);
        }
        if (trails.Count == 0)
        {
            D3D.ClearState(context);
            Hide();
        }
    }

    private GpuTrailRenderer()
    {
        try
        {
            D3D.ValidateAbi();
            if (sizeof(FrameConstants) != 192 || sizeof(TrailGpuVertex) != 32) throw new NotSupportedException("Trail shader layout mismatch.");
            // Hardware only: no WARP fallback that disguises CPU rendering.
            D3D.Check(D3D.D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out device, out uint featureLevel, out context));
            if (featureLevel < 0xa000) throw new NotSupportedException("Direct3D feature level 10.0 or later is required.");
            window = NativeSurface.Create();
            nint dxgi = 0, adapter = 0, factory = 0;
            try
            {
                dxgi = D3D.Query(device, D3D.DxgiDeviceId);
                adapter = D3D.Adapter(dxgi);
                factory = D3D.Factory(adapter);
                swap = D3D.SwapChain(factory, device, width, height);
                D3D.Check(D3D.DCompositionCreateDevice(dxgi, D3D.CompositionDeviceId, out composition));
                target = D3D.CompositionTarget(composition, window);
                visual = D3D.CompositionVisual(composition);
                D3D.SetContent(visual, swap);
                D3D.SetRoot(target, visual);
                D3D.Commit(composition);
            }
            finally
            {
                D3D.Release(ref factory);
                D3D.Release(ref adapter);
                D3D.Release(ref dxgi);
            }
            CreateRenderTarget();
            nint vs = 0, ps = 0;
            try
            {
                vs = D3D.Compile(ShaderSource, "VS", "vs_4_0");
                ps = D3D.Compile(ShaderSource, "PS", "ps_4_0");
                vertexShader = D3D.Shader(device, vs, true);
                pixelShader = D3D.Shader(device, ps, false);
                layout = D3D.Layout(device, vs);
            }
            finally
            {
                D3D.Release(ref ps);
                D3D.Release(ref vs);
            }
            constants = D3D.Buffer(device, checked((uint)sizeof(FrameConstants)), 4);
            blend = D3D.Blend(device);
            raster = D3D.Raster(device);
            sampler = D3D.Sampler(device);
            Log($"Direct3D 11 hardware renderer initialized; feature level 0x{featureLevel:X}.");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public static GpuTrailRenderer? TryCreate()
    {
        try
        {
            return new GpuTrailRenderer();
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or NotSupportedException or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            Log("GPU initialization failed; WPF trail fallback enabled. " + ex);
            return null;
        }
    }

    public static void Log(string message)
    {
        Debug.WriteLine("Taco Stuffer GPU: " + message);
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TacoStuffer");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "renderer.log");
            if (File.Exists(file) && new FileInfo(file).Length > 1_000_000) File.WriteAllText(file, "");
            File.AppendAllText(file, DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(ex.Message);
        }
    }

    private void CreateRenderTarget()
    {
        nint buffer = D3D.BackBuffer(swap);
        try
        {
            renderTarget = D3D.ResourceView(device, buffer, true);
        }
        finally
        {
            D3D.Release(ref buffer);
        }
    }

    public void Hide()
    {
        if (disposed) return;
        if (shown) NativeSurface.ShowWindow(window, 0);
        shown = false;
        dirty = true;
    }

    public void Render(NativeGameWindow.Rect placement, nint above, MumbleSnapshot camera,
        IReadOnlyList<WorldTrail> trails, string? folder, IReadOnlyDictionary<string, Vector3>? preview, bool cameraChanged)
    {
        if (disposed) return;
        bool fullMap = (camera.UiState & 1) != 0;
        uint newWidth = (uint)Math.Max(1, placement.Right - placement.Left), newHeight = (uint)Math.Max(1, placement.Bottom - placement.Top);
        bool mapReady = MapRibbonProjection.TryBasis(camera, newWidth, newHeight, out var mapBasis);
        if (!trails.Any(t => t.Trail.MapId == camera.MapId && ((!fullMap && t.WorldVisible) || (mapReady && (fullMap ? t.MapVisible : t.MinimapVisible)))))
        {
            Hide();
            return;
        }
        if (folder != projectRoot)
        {
            projectRoot = folder;
            foreach (var tex in textures.Values) tex.Dispose();
            textures.Clear();
            SceneChanged();
        }
        if (width != newWidth || height != newHeight)
        {
            D3D.Targets(context, 0);
            D3D.Release(ref renderTarget);
            D3D.Resize(swap, newWidth, newHeight);
            width = newWidth;
            height = newHeight;
            CreateRenderTarget();
            dirty = true;
        }
        mapReady = MapRibbonProjection.TryBasis(camera, width, height, out mapBasis);
        bool reposition = presentationRepair || !shown || !NativeGameWindow.IsWindowVisible(window)
            || !NativeGameWindow.IsTopmost(window) || !bounds.Equals(placement) || previousAbove != above;
        if (reposition)
        {
            if (NativeGameWindow.PlaceOverlay(window, above, placement.Left, placement.Top, (int)width, (int)height, shown))
            {
                bounds = placement;
                previousAbove = above;
                presentationRepair = false;
            }
            // Reordering existing DirectComposition content is independent of
            // scene/mesh/texture invalidation. Preserve all cached buffers.
        }
        if (!dirty && !cameraChanged && shown && !trails.Any(t => t.Trail.MapId == camera.MapId && t.AnimSpeed != 0 && ((!fullMap && t.WorldVisible) || (mapReady && (fullMap ? t.MapVisible : t.MinimapVisible))))) return;
        long started = Stopwatch.GetTimestamp();
        int rebuilt = 0, draws = 0;
        // Resolve selection keys once; touching one segment never rebuilds the
        // remaining pack. Dictionary identity changes on each drag sample.
        previewSegments.Clear();
        if (preview != null) foreach (var key in preview.Keys)
            if (TrailPointRef.Parse(key) is { } p) previewSegments.Add(new(p.Trail, p.Segment));
        activeSegments.Clear();
        D3D.Targets(context, renderTarget);
        D3D.Clear(context, renderTarget);
        D3D.Pipeline(context, vertexShader, pixelShader, layout, constants, blend, raster, sampler, width, height);
        var frame = MakeFrame(camera);
        foreach (var line in trails)
        {
            // Retain off-map caches, but prune deleted trails/segments below.
            foreach (var segment in line.Trail.Segments) activeSegments.Add(segment);
            if (line.Trail.MapId != camera.MapId) continue;
            bool worldVisible = !fullMap && line.WorldVisible;
            bool mapVisible = mapReady && (fullMap ? line.MapVisible : line.MinimapVisible);
            if (!worldVisible && !mapVisible) continue;
            var texture = GetTexture(line.Texture);
            frame.Tint = new(line.Color.R / 255f, line.Color.G / 255f, line.Color.B / 255f, line.Color.A / 255f);
            frame.Origin.W = Math.Clamp(line.Alpha, 0, 1);
            frame.View.W = VisualAttributeMath.AnimationOffset(Stopwatch.GetElapsedTime(animationStart).TotalSeconds, line.AnimSpeed);
            frame.Style = new(Math.Max(.001f, Math.Abs(line.Scale)) * 2 / line.GameWidth, line.Height, line.FadeNear, line.FadeFar);
            float mapWidth = MapRibbonProjection.Width(line.MapSize, camera.Compass?.UiSize ?? 1) * (fullMap ? line.MapWidth : line.MinimapWidth);
            frame.MapOrigin = new(mapBasis.Origin.X, mapBasis.Origin.Y, mapBasis.WorldOrigin.X, mapBasis.WorldOrigin.Y);
            frame.MapX = new(mapBasis.X, 0, 0);
            frame.MapZ = new(mapBasis.Z, 0, 0);
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 0 ? !worldVisible : !mapVisible) continue;
                frame.MapStyle = new(mapWidth, MapRibbonProjection.UvScale(mapBasis, mapWidth, line.Scale), pass, line.GameWidth);
                D3D.Update(context, constants, &frame);
                foreach (var segment in line.Trail.Segments)
                {
                    var segmentPreview = previewSegments.Contains(new(line.Trail.Number, segment.Id)) ? preview : null;
                    if (!meshes.TryGetValue(segment, out var mesh)) meshes[segment] = mesh = new();
                    if (mesh.Count != segment.Points.Count || mesh.Revision != segment.GeometryRevision || !ReferenceEquals(mesh.Source, segment.Points) || !ReferenceEquals(mesh.Preview, segmentPreview))
                    {
                        UpdateMesh(mesh, line.Trail, segment, segmentPreview);
                        rebuilt++;
                    }
                    if (mesh.VertexCount == 0) continue;
                    D3D.Draw(context, mesh.Vertices, mesh.Indices, texture.View, (uint)(mesh.VertexCount / 4 * 6));
                    draws++;
                }
            }
        }
        foreach (var segment in meshes.Keys.Where(s => !activeSegments.Contains(s)).ToArray())
        {
            meshes[segment].Dispose();
            meshes.Remove(segment);
        }
        bool presented = D3D.Present(swap, out bool occluded);
        if (!shown && (presented || occluded))
        {
            // Normally show after the first frame. If a hidden swap chain is
            // reported occluded, expose its HWND so the next present can resume.
            shown = NativeGameWindow.PlaceOverlay(window, above, placement.Left, placement.Top, (int)width, (int)height, true);
        }
        dirty = !presented;
        checkTextures = false;
        double ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (Environment.TickCount64 - lastTiming >= 2000)
        {
            lastTiming = Environment.TickCount64;
            Debug.WriteLine($"Taco Stuffer GPU submit: {ms:F2} ms; {draws} segment draws; {rebuilt} mesh updates; {meshes.Count} cached segments.");
        }
    }

    private void UpdateMesh(Mesh mesh, EditableTrail trail, TrailSegment segment, IReadOnlyDictionary<string, Vector3>? preview)
    {
        bool append = mesh.Count >= 2 && segment.Points.Count > mesh.Count && ReferenceEquals(mesh.Source, segment.Points)
            && preview == null && mesh.Preview == null && segment.GeometryRevision - mesh.Revision == segment.Points.Count - mesh.Count;
        var vertices = GpuTrailGeometry.Build(trail, segment, preview, append ? mesh.Count : 1, append ? mesh.Length : 0);
        int required = checked((append ? mesh.VertexCount : 0) + vertices.Length);
        if (required > mesh.Capacity)
        {
            // Default-usage buffers support append uploads. Grow geometrically;
            // only growth boundaries rebuild the complete CPU segment mesh.
            int capacity = 4;
            while (capacity < required) capacity = checked(capacity * 2);
            nint newVertices = 0, newIndices = 0;
            try
            {
                newVertices = D3D.Buffer(device, checked((uint)(capacity * sizeof(TrailGpuVertex))), 1);
                uint[] indices = GpuTrailGeometry.Indices(capacity / 4);
                fixed (uint* data = indices) newIndices = D3D.Buffer(device, checked((uint)(indices.Length * sizeof(uint))), 2, data);
                // Rebuild once at growth boundaries; normal append samples only
                // upload their added edges. No camera-frame CPU geometry work.
                if (append)
                {
                    vertices = GpuTrailGeometry.Build(trail, segment);
                    append = false;
                    required = vertices.Length;
                }
                D3D.Release(ref mesh.Vertices);
                D3D.Release(ref mesh.Indices);
                mesh.Vertices = newVertices;
                newVertices = 0;
                mesh.Indices = newIndices;
                newIndices = 0;
                mesh.Capacity = capacity;
            }
            finally
            {
                D3D.Release(ref newIndices);
                D3D.Release(ref newVertices);
            }
        }
        if (vertices.Length > 0)
        {
            fixed (TrailGpuVertex* data = vertices)
            D3D.UpdateRange(context, mesh.Vertices, checked((uint)((append ? mesh.VertexCount : 0) * sizeof(TrailGpuVertex))), checked((uint)(vertices.Length * sizeof(TrailGpuVertex))), data);
        }
        mesh.VertexCount = required;
        mesh.Count = segment.Points.Count;
        mesh.Revision = segment.GeometryRevision;
        mesh.Source = segment.Points;
        mesh.Preview = preview;
        mesh.Length = vertices.Length > 0 ? -vertices[^1].Uv.Y : append ? mesh.Length : 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameConstants
    {
        public Vector4 Origin, Right, Up, Forward, Tint, Style, MapRect, View, MapOrigin, MapX, MapZ, MapStyle;
    }

    private FrameConstants MakeFrame(MumbleSnapshot camera)
    {
        var forward = Vector3.Normalize(camera.CameraFront);
        var right = Vector3.Cross(Vector3.UnitY, forward);
        right = right.LengthSquared() < .000001f ? Vector3.UnitX : Vector3.Normalize(right);
        var up = Vector3.Normalize(Vector3.Cross(forward, right));
        float fov = camera.Fov is > .1f and < 3 ? camera.Fov : CameraProjection.DefaultFov;
        float focus = 1 / MathF.Tan(fov * .5f);
        var map = MapProjection.Viewport(camera, width, height);
        return new()
        {
            Origin = new(camera.CameraPosition, 1), Right = new(right, focus * height / width), Up = new(up, focus), Forward = new(forward, CameraProjection.NearClip),
            MapRect = new(map.X, map.Y, map.X + map.Width, map.Y + map.Height), View = new(width, height, 100000, 0)
        };
    }

    private TextureAsset GetTexture(string relative)
    {
        textures.TryGetValue(relative, out var cached);
        if (cached != null && !checkTextures) return cached;
        string key = relative;
        string? path = null;
        DateTime stamp = DateTime.MinValue;
        if (projectRoot != null && relative.Length > 0)
        {
            try
            {
                string root = Path.GetFullPath(projectRoot) + Path.DirectorySeparatorChar;
                string candidate = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
                if (candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)) path = candidate;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                Debug.WriteLine(ex.Message);
            }
        }
        try
        {
            if (path != null) stamp = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(ex.Message);
        }
        if (cached != null && cached.Stamp == stamp) return cached;
        BitmapSource image;
        try
        {
            if (path == null) image = DefaultTexture();
            else
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();
                image = bitmap;
            }
        }
        catch (Exception ex) when (ex is IOException or System.IO.FileFormatException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Log($"Texture '{relative}' could not be decoded; using preview texture. {ex.Message}");
            image = DefaultTexture();
        }
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int stride = checked(converted.PixelWidth * 4);
        byte[] pixels = new byte[checked(stride * converted.PixelHeight)];
        converted.CopyPixels(pixels, stride, 0);
        nint resource = 0, view = 0;
        try
        {
            fixed (byte* bytes = pixels) resource = D3D.Texture(device, (uint)converted.PixelWidth, (uint)converted.PixelHeight, bytes);
            view = D3D.ResourceView(device, resource, false);
            var texture = new TextureAsset { Resource = resource, View = view, Stamp = stamp };
            resource = view = 0;
            cached?.Dispose();
            textures[key] = texture;
            return texture;
        }
        finally
        {
            D3D.Release(ref view);
            D3D.Release(ref resource);
        }
    }

    private static BitmapSource DefaultTexture()
    {
        var brush = ((DrawingBrush)Application.Current.FindResource("DefaultTrailPreview")).Clone();
        brush.Viewport = new Rect(0, 0, 32, 32);
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen()) dc.DrawRectangle(brush, null, new Rect(0, 0, 32, 32));
        var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        bitmap.Freeze();
        return bitmap;
    }

    private sealed class Mesh : IDisposable
    {
        public nint Vertices, Indices;
        public int Capacity, VertexCount, Count;
        public long Revision = -1;
        public float Length;
        public List<TrailPoint>? Source;
        public IReadOnlyDictionary<string, Vector3>? Preview;
        public void Dispose()
        {
            D3D.Release(ref Indices);
            D3D.Release(ref Vertices);
        }
    }

    private sealed class TextureAsset : IDisposable
    {
        public nint Resource, View;
        public DateTime Stamp;
        public void Dispose()
        {
            D3D.Release(ref View);
            D3D.Release(ref Resource);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        Hide();
        disposed = true;
        if (context != 0) D3D.ClearState(context);
        foreach (var mesh in meshes.Values) mesh.Dispose();
        meshes.Clear();
        foreach (var tex in textures.Values) tex.Dispose();
        textures.Clear();
        D3D.Release(ref visual);
        D3D.Release(ref target);
        D3D.Release(ref composition);
        D3D.Release(ref renderTarget);
        D3D.Release(ref swap);
        D3D.Release(ref sampler);
        D3D.Release(ref raster);
        D3D.Release(ref blend);
        D3D.Release(ref constants);
        D3D.Release(ref layout);
        D3D.Release(ref pixelShader);
        D3D.Release(ref vertexShader);
        D3D.Release(ref context);
        D3D.Release(ref device);
        if (window != 0)
        {
            NativeSurface.DestroyWindow(window);
            window = 0;
        }
    }

    private const string ShaderSource = """
        cbuffer Frame : register(b0) {
            float4 origin; float4 cameraRight; float4 cameraUp; float4 cameraForward;
            float4 tint; float4 style; float4 mapRect; float4 view;
            float4 mapOrigin; float4 mapX; float4 mapZ; float4 mapStyle;
        };
        Texture2D trailTexture : register(t0);
        SamplerState repeatTexture : register(s0);
        struct VertexIn { float3 position : POSITION; float2 uv : TEXCOORD0; float3 center : TEXCOORD1; };
        struct VertexOut { float4 position : SV_POSITION; float2 uv : TEXCOORD0; float3 world : TEXCOORD1; };
        VertexOut VS(VertexIn input) {
            VertexOut output;
            if (mapStyle.z > 0.5) {
                float2 center = mapOrigin.xy + (input.center.x - mapOrigin.z) * mapX.xy + (input.center.z - mapOrigin.w) * mapZ.xy;
                float2 side = (input.position.x - input.center.x) * mapX.xy + (input.position.z - input.center.z) * mapZ.xy;
                float lengthSquared = dot(side, side);
                side = lengthSquared > 1e-12 ? side * rsqrt(lengthSquared) : float2(1, 0);
                float2 screen = center + side * mapStyle.x * 0.5;
                output.position = float4(screen.x * 2 / view.x - 1, 1 - screen.y * 2 / view.y, 0, 1);
                output.uv = float2(input.uv.x, input.uv.y * mapStyle.y);
                output.world = input.center;
                return output;
            }
            float3 p = input.center + (input.position - input.center) * mapStyle.w + float3(0, style.y, 0);
            float3 relative = p - origin.xyz;
            float z = dot(relative, cameraForward.xyz);
            float nearPlane = cameraForward.w, farPlane = view.z;
            output.position = float4(dot(relative, cameraRight.xyz) * cameraRight.w,
                dot(relative, cameraUp.xyz) * cameraUp.w,
                (z * farPlane - nearPlane * farPlane) / (farPlane - nearPlane), z);
            output.uv = float2(input.uv.x, input.uv.y * style.x);
            output.world = p;
            return output;
        }
        float4 PS(VertexOut input) : SV_TARGET {
            bool insideMap = input.position.x >= mapRect.x && input.position.x < mapRect.z &&
                input.position.y >= mapRect.y && input.position.y < mapRect.w;
            if (mapStyle.z > 0.5 ? !insideMap : insideMap) discard;
            float alpha = saturate(origin.w);
            if (mapStyle.z < 0.5 && style.z >= 0 && style.w >= style.z) {
                float distanceInches = distance(input.world, origin.xyz) / 0.0254;
                alpha *= style.w == style.z ? (distanceInches <= style.z ? 1 : 0) :
                    saturate((style.w - distanceInches) / (style.w - style.z));
            }
            float4 color = trailTexture.Sample(repeatTexture, input.uv + float2(0, view.w)) * tint;
            color.a *= alpha;
            color.rgb *= color.a;
            return color;
        }
        """;

    private static class NativeSurface
    {
        private const string ClassName = "TacoStuffer.Direct3DTrails";
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam);
        private static readonly WindowProcedure Procedure = WndProc;
        private static ushort atom;
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowClass
        {
            public uint Size, Style;
            public nint Procedure;
            public int ClassExtra, WindowExtra;
            public nint Instance, Icon, Cursor, Background;
            public string? Menu, Name;
            public nint SmallIcon;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern ushort RegisterClassExW(in WindowClass description);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern nint CreateWindowExW(uint extended, string name, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(nint hwnd, int command);

        [DllImport("user32.dll")]
        internal static extern bool DestroyWindow(nint hwnd);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(nint hwnd, uint colorKey, byte alpha, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern nint GetModuleHandleW(string? name);

        internal static nint Create()
        {
            nint instance = GetModuleHandleW(null);
            if (atom == 0)
            {
                var description = new WindowClass { Size = (uint)Marshal.SizeOf<WindowClass>(), Procedure = Marshal.GetFunctionPointerForDelegate(Procedure), Instance = instance, Name = ClassName };
                atom = RegisterClassExW(description);
                if (atom == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            // WS_EX_TRANSPARENT on a layered window passes mouse input to
            // other processes. HTTRANSPARENT alone only searches this thread;
            // WS_DISABLED can block input instead of forwarding it to GW2.
            const uint noRedirectionBitmap = 0x00200000, noActivate = 0x08000000;
            const uint toolWindow = 0x80, transparent = 0x20, layered = 0x00080000;
            const uint popup = 0x80000000, alphaFlag = 2;
            nint hwnd = CreateWindowExW(noRedirectionBitmap | noActivate | toolWindow | transparent | layered,
                ClassName, "Taco Stuffer Trails", popup, 0, 0, 1, 1, 0, 0, instance, 0);
            if (hwnd == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            // Global opacity stays 255: DirectComposition still provides the
            // ribbon's per-pixel alpha. No UpdateLayeredWindow/readback path.
            if (!SetLayeredWindowAttributes(hwnd, 0, 255, alphaFlag))
            {
                int error = Marshal.GetLastWin32Error();
                DestroyWindow(hwnd);
                throw new Win32Exception(error);
            }
            return hwnd;
        }

        private static nint WndProc(nint hwnd, uint message, nint wParam, nint lParam) => message switch
        {
        0x84 => -1, // Supplementary same-thread hit-test pass-through.
        0x21 => 3,  // MA_NOACTIVATE
        0x14 => 1,  // No background erase: DirectComposition owns pixels.
        _ => DefWindowProcW(hwnd, message, wParam, lParam)
        };
    }
}
