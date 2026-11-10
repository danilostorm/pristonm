using System.Buffers.Binary;

namespace StormPT.MobileLab;

/// <summary>
/// Read-only projection of the legacy 28-byte TRANS_PLAYPOS packet.
/// This is a diagnostic model, NOT an accepted movement command.
/// FieldCode is sent as a uint32. The original C++ client sends
/// unsigned -1 (0xFFFFFFFF) when the player is not in a stage.
/// </summary>
public sealed record DesktopPositionObservation(
    uint RawAreaCode,
    int RawX,
    int RawY,
    int RawZ,
    short CurrentHp,
    short MaximumHp)
{
    public const int FixedPointShift = 8;
    public const int FixedPointFactor = 1 << FixedPointShift;

    public bool HasArea => RawAreaCode != uint.MaxValue;

    public uint? AreaCode => HasArea ? RawAreaCode : null;

    public double X => RawX / (double)FixedPointFactor;
    public double Y => RawY / (double)FixedPointFactor;
    public double Z => RawZ / (double)FixedPointFactor;

    // Mirrors a basic plausibility rule observed in the old handler.
    // It is a diagnostic flag, not sufficient to authorize changes to HP.
    public bool BasicHpPlausibility =>
        MaximumHp >= 0 && CurrentHp >= 0 &&
        CurrentHp <= MaximumHp * 2;
}

/// <summary>
/// Fail-closed structural reader only. Does not send a command, mutate a
/// database, update the game server, authenticate, or validate map boundaries.
/// The native server must be the sole authority for movement and character HP.
/// </summary>
public static class DesktopPositionDecoder
{
    public const int PositionOpcode = 23; // enum OpCode (observed source build)
    public const int PositionFrameBytes = 28; // sizeof(TRANS_PLAYPOS), x86/x64 layout

    public static DesktopPositionObservation Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != PositionFrameBytes)
            throw new FormatException("Expected a 28-byte TRANS_PLAYPOS packet.");

        int declaredSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(0, 4));
        int opcode = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(4, 4));
        if (declaredSize != PositionFrameBytes || opcode != PositionOpcode)
            throw new FormatException("Invalid movement packet header.");

        // Never trust these client-supplied values as authoritative.
        var area = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(8, 4));
        var x = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(12, 4));
        var y = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(16, 4));
        var z = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(20, 4));
        var hp = BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(24, 2));
        var maxHp = BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(26, 2));
        return new DesktopPositionObservation(area, x, y, z, hp, maxHp);
    }
}

/// <summary>
/// Bounded public JSON projection for future Web/mobile display of an already
/// authorized *server-derived* observation. Cannot grant world access.
/// </summary>
public sealed record DesktopPositionView(
    uint? AreaCode,
    double X,
    double Y,
    double Z,
    bool BasicHpPlausibility,
    bool WorldAccessGranted)
{
    public static DesktopPositionView FromObservation(DesktopPositionObservation observation)
        => new(
            observation.AreaCode,
            observation.X,
            observation.Y,
            observation.Z,
            observation.BasicHpPlausibility,
            WorldAccessGranted: false);
}
