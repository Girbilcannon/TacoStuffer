using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace TacoStuffer.Models;

public sealed record MumbleSnapshot(uint Tick, uint ProcessId, uint MapId, uint MapType, uint UiState,
    string CharacterName, Vector3 AvatarPosition, Vector3 CameraPosition, Vector3 CameraFront, Vector3 CameraTop, float Fov)
{
    // Windows UTF-16 layout, checked against the supplied TacO MumbleLink.h.
    public CompassTelemetry? Compass { get; init; }
    public const int FrameSize = 1364;
    // Allocate the full Windows LinkedMem layout, including the UTF-16 description.
    // The decoder only needs the prefix above; GW2 may map/write the entire structure.
    public const int MappingSize = FrameSize + 2048 * 2; // 5460 bytes
    public static MumbleSnapshot? Decode(ReadOnlySpan<byte> data)
    {
        // GW2 declares 48 context bytes for Mumble's instance matching but
        // writes its UI state and process ID beyond that declared length.
        if (data.Length < FrameSize || U32(data, 0) != 2 || U32(data, 1104) < 48 || U32(data, 1104) > 256) return null;
        uint map = U32(data, 1136), pid = U32(data, 1188);
        if (map == 0 || pid == 0) return null;
        var avatar = Vector(data, 8);
        var camera = Vector(data, 556);
        var front = Vector(data, 568);
        var top = Vector(data, 580);
        if (!Finite(avatar) || !Finite(camera) || !Finite(front) || !Finite(top) || avatar == Vector3.Zero || front.LengthSquared() < 0.01f) return null;
        string identity = Encoding.Unicode.GetString(data.Slice(592, 512)).Split('\0')[0];
        try
        {
            using var json = JsonDocument.Parse(identity);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("map_id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetUInt32(out uint identityMap) || identityMap != map) return null;
            if (!root.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString())) return null;
            float fov = root.TryGetProperty("fov", out var f) && f.ValueKind == JsonValueKind.Number && f.TryGetSingle(out float value) && float.IsFinite(value) && value > 0.1f && value < 3 ? value : CameraProjection.DefaultFov;
            return new(U32(data, 4), pid, map, U32(data, 1140), U32(data, 1156), name.GetString()!, avatar, camera, front, top, fov)
            {
                Compass = new CompassTelemetry(BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(1160, 2)), BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(1162, 2)), Float(data, 1164), new(Float(data, 1168), Float(data, 1172)), new(Float(data, 1176), Float(data, 1180)), Float(data, 1184), root.TryGetProperty("uisz", out var ui) && ui.ValueKind == JsonValueKind.Number && ui.TryGetInt32(out int size) ? size : 1)
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));

    private static float Float(ReadOnlySpan<byte> data, int offset) => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4)));

    private static Vector3 Vector(ReadOnlySpan<byte> data, int offset) => new(Float(data, offset), Float(data, offset + 4), Float(data, offset + 8));

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}

public sealed class MumbleSession
{
    private MumbleSnapshot? previous;
    private long lastAdvance;
    private long candidateSince;
    private bool observedAdvance;
    public bool IsLive { get; private set; }
    public bool IsPlayable { get; private set; }
    public void Observe(MumbleSnapshot? frame, long milliseconds)
    {
        if (frame == null)
        {
            Reset();
            return;
        }
        bool changed = previous == null || previous.ProcessId != frame.ProcessId || previous.MapId != frame.MapId || previous.MapType != frame.MapType || previous.CharacterName != frame.CharacterName;
        if (changed)
        {
            candidateSince = milliseconds;
            observedAdvance = false;
            lastAdvance = milliseconds;
        }
        else if (previous!.Tick != frame.Tick)
        {
            observedAdvance = true;
            lastAdvance = milliseconds;
        }
        IsLive = observedAdvance && milliseconds - lastAdvance < 1500;
        // Conservative readiness gate: stable character/map, moving tick and
        // playable map type: exclude redirect (0), character creation (1) and
        // unknown (-1). MumbleLink has no explicit loading-screen flag.
        IsPlayable = IsLive && frame.MapType > 1 && frame.MapType != uint.MaxValue && milliseconds - candidateSince >= 500;
        if (!IsLive && observedAdvance)
        {
            candidateSince = milliseconds;
            observedAdvance = false;
        }
        previous = frame;
    }

    public void Reset()
    {
        previous = null;
        observedAdvance = false;
        IsLive = IsPlayable = false;
    }
}
