using System.Buffers.Binary;

namespace StormPT.MobileLab;

/// <summary>
/// A strict offline parser for the 3 required float fields in NFMsg.Position
/// (NFMsgBase.proto). This is NOT a network message envelope, complete
/// ReqAckPlayerMove message, gameplay movement command or authentication.
/// </summary>
public sealed record MobilePositionSample(float X, float Y, float Z);

public static class MobilePositionProtoCodec
{
    public const int ProtoPayloadBytes = 15;
    private const byte XTag = 0x0D; // field 1, wire type 5 (fixed32)
    private const byte YTag = 0x15; // field 2, wire type 5 (fixed32)
    private const byte ZTag = 0x1D; // field 3, wire type 5 (fixed32)

    public static byte[] EncodeFromDesktop(DesktopPositionObservation source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Encode(new MobilePositionSample(
            checked((float)source.X),
            checked((float)source.Y),
            checked((float)source.Z)));
    }

    public static byte[] Encode(MobilePositionSample input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!float.IsFinite(input.X) || !float.IsFinite(input.Y) ||
            !float.IsFinite(input.Z))
            throw new FormatException("Mobile coordinate must be finite.");

        var buffer = new byte[ProtoPayloadBytes];
        WriteFloat(buffer.AsSpan(0, 5), XTag, input.X);
        WriteFloat(buffer.AsSpan(5, 5), YTag, input.Y);
        WriteFloat(buffer.AsSpan(10, 5), ZTag, input.Z);
        return buffer;
    }

    public static MobilePositionSample Decode(ReadOnlySpan<byte> input)
    {
        if (input.Length != ProtoPayloadBytes)
            throw new FormatException("Expected the three required NFMsg.Position fields.");

        float x = 0, y = 0, z = 0;
        int seen = 0;
        for (int offset = 0; offset < ProtoPayloadBytes; offset += 5)
        {
            byte tag = input[offset];
            int flag = tag switch
            {
                XTag => 1,
                YTag => 2,
                ZTag => 4,
                _ => throw new FormatException("Unexpected field or protobuf wire type.")
            };
            if ((seen & flag) != 0)
                throw new FormatException("Duplicate NFMsg.Position field.");

            seen |= flag;
            float value = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(input.Slice(offset + 1, 4)));
            if (!float.IsFinite(value))
                throw new FormatException("Non-finite NFMsg.Position float.");

            switch (flag)
            {
                case 1: x = value; break;
                case 2: y = value; break;
                case 4: z = value; break;
            }
        }
        if (seen != 7)
            throw new FormatException("Missing required NFMsg.Position coordinate.");
        return new MobilePositionSample(x, y, z);
    }

    private static void WriteFloat(Span<byte> output, byte tag, float number)
    {
        output[0] = tag;
        BinaryPrimitives.WriteInt32LittleEndian(
            output.Slice(1, 4), BitConverter.SingleToInt32Bits(number));
    }
}
