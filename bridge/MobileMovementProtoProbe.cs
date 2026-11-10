namespace StormPT.MobileLab;

/// <summary>
/// Decoded offline view of NFMsg.ReqAckPlayerMove. This is NOT a native StormPT
/// movement request, login, game-world admission, or authorization.
/// </summary>
internal sealed record MobileMoveObservation(
    MobileRuntimeEntity Mover,
    int MoveType,
    IReadOnlyList<MobilePositionSample> TargetPositions,
    IReadOnlyList<MobilePositionSample> SourcePositions);

/// <summary>
/// Defensive Protobuf wire inspector for the four fields actually declared
/// by NFMsgShare.proto: ReqAckPlayerMove.
///   1 mover:       required NFMsg.Ident (svrid,index int64)
///   2 moveType:    required int32
///   3 target_pos:  repeated NFMsg.Position (float x,y,z)
///   4 source_pos:  repeated NFMsg.Position (float x,y,z)
///
/// No unknown fields, nested extensions, network writes, or user-provided
/// movement authorization. Parser is deliberately stricter than generic PB.
/// </summary>
internal static class MobileMovementProtoProbe
{
    private const int MaxMessageBytes = 4096;
    private const int MaxPositionsPerList = 16;
    private const int MaxFieldCount = 64;

    internal static MobileMoveObservation Decode(ReadOnlySpan<byte> packet)
    {
        if (packet.IsEmpty || packet.Length > MaxMessageBytes)
            throw new FormatException("Invalid movement packet size.");
        int offset = 0;
        int fields = 0;
        bool gotMover = false, gotType = false;
        MobileRuntimeEntity mover = default;
        int moveType = 0;
        var target = new List<MobilePositionSample>();
        var source = new List<MobilePositionSample>();

        while (offset < packet.Length)
        {
            if (++fields > MaxFieldCount)
                throw new FormatException("Excessive movement fields.");
            ulong tag = ReadVarint(packet, ref offset);
            int field = checked((int)(tag >> 3));
            int wire = (int)(tag & 7);
            if (field == 0) throw new FormatException("Invalid field number.");

            switch (field)
            {
                case 1 when wire == 2:
                    if (gotMover) throw new FormatException("Repeated mover identity.");
                    mover = ReadIdent(ReadBlock(packet, ref offset));
                    gotMover = true;
                    break;
                case 2 when wire == 0:
                    if (gotType) throw new FormatException("Repeated moveType.");
                    var type = ReadVarint(packet, ref offset);
                    // This LAB probe only supports non-negative int32 values.
                    if (type > int.MaxValue)
                        throw new FormatException("Unexpected movement type value.");
                    moveType = (int)type;
                    gotType = true;
                    break;
                case 3 when wire == 2:
                    if (target.Count >= MaxPositionsPerList)
                        throw new FormatException("Too many target positions.");
                    target.Add(MobilePositionProtoCodec.Decode(
                        ReadBlock(packet, ref offset)));
                    break;
                case 4 when wire == 2:
                    if (source.Count >= MaxPositionsPerList)
                        throw new FormatException("Too many source positions.");
                    source.Add(MobilePositionProtoCodec.Decode(
                        ReadBlock(packet, ref offset)));
                    break;
                default:
                    throw new FormatException("Unexpected movement field/wire type.");
            }
        }

        if (!gotMover || !gotType)
            throw new FormatException("Movement mover/type is required.");

        return new MobileMoveObservation(mover, moveType,
            target.ToArray(), source.ToArray());
    }

    internal static bool IsBoundToVerifiedSession(
        MobileMoveObservation observation,
        LabIdentityCrosswalk crosswalk, Guid serverEpoch,
        string verifiedSessionKey, out uint nativeSerial)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(crosswalk);
        // Must be called only AFTER server-side session verification.
        // This check never grants authority to update position or HP.
        return crosswalk.TryResolveMobile(serverEpoch, verifiedSessionKey,
            observation.Mover, out nativeSerial);
    }

    private static MobileRuntimeEntity ReadIdent(ReadOnlySpan<byte> msg)
    {
        int offset = 0;
        bool hasServer = false, hasIndex = false;
        long server = 0, index = 0;

        while (offset < msg.Length)
        {
            ulong tag = ReadVarint(msg, ref offset);
            if (tag == 8) // int64 svrid=1, wire0
            {
                if (hasServer) throw new FormatException("Repeated mobile server id.");
                ulong value = ReadVarint(msg, ref offset);
                if (value > long.MaxValue)
                    throw new FormatException("Invalid Mobile Ident.svrid.");
                server = (long)value;
                hasServer = true;
            }
            else if (tag == 16) // int64 index=2, wire0
            {
                if (hasIndex) throw new FormatException("Repeated mobile index.");
                ulong value = ReadVarint(msg, ref offset);
                if (value == 0 || value > long.MaxValue)
                    throw new FormatException("Invalid Mobile Ident.index.");
                index = (long)value;
                hasIndex = true;
            }
            else
                throw new FormatException("Unexpected Mobile Ident field.");
        }
        if (!hasServer || !hasIndex)
            throw new FormatException("Incomplete Mobile Ident.");
        return new MobileRuntimeEntity(server, index);
    }

    private static ReadOnlySpan<byte> ReadBlock(ReadOnlySpan<byte> bytes, ref int offset)
    {
        ulong length = ReadVarint(bytes, ref offset);
        if (length > MaxMessageBytes || length > (ulong)(bytes.Length - offset))
            throw new FormatException("Truncated/oversized Protobuf nested message.");
        int start = offset;
        offset += (int)length;
        return bytes.Slice(start, (int)length);
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> bytes, ref int offset)
    {
        ulong value = 0;
        for (int shift = 0; shift < 70; shift += 7)
        {
            if (offset >= bytes.Length)
                throw new FormatException("Truncated protobuf varint.");
            byte b = bytes[offset++];
            if (shift == 63 && b > 1)
                throw new FormatException("Varint overflow.");
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return value;
        }
        throw new FormatException("Protobuf varint too long.");
    }
}
