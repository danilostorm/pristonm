using System.Buffers.Binary;
using StormPT.MobileLab;

static class Test
{
    private static int passed;

    private static byte[] Packet(int opcode, byte[] payload)
    {
        var bytes = new byte[DesktopWireFramer.HeaderBytes + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), opcode);
        payload.AsSpan().CopyTo(bytes.AsSpan(8));
        return bytes;
    }

    private static void Check(bool condition, string caseName)
    {
        if (!condition) throw new Exception("FAILED: " + caseName);
        passed++;
    }

    private static void Throws<T>(Action action, string caseName)
        where T : Exception
    {
        try { action(); }
        catch (T) { passed++; return; }
        throw new Exception("FAILED (not rejected): " + caseName);
    }

    private static byte[] Position(uint area, int x, int y, int z, short hp, short maxHp)
    {
        var bytes = new byte[DesktopPositionDecoder.PositionFrameBytes];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4),
            DesktopPositionDecoder.PositionOpcode);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), area);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12, 4), x);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16, 4), y);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20, 4), z);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(24, 2), hp);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26, 2), maxHp);
        return bytes;
    }

    public static int Main()
    {
        var f = new DesktopWireFramer();

        // A valid empty-body frame has a total length of 8.
        var minimum = Packet(23, Array.Empty<byte>());
        var minimumResult = f.Feed(minimum);
        Check(minimumResult.Count == 1 &&
              minimumResult[0] == new DesktopFrameInfo(8, 23, 0),
              "minimum frame header");

        // smTRANS_COMMAND: int32 size, opcode, LParam, WParam, SParam, EParam.
        var commandPayload = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(commandPayload.AsSpan(0, 4), 17);
        BinaryPrimitives.WriteInt32LittleEndian(commandPayload.AsSpan(4, 4), -4);
        BinaryPrimitives.WriteInt32LittleEndian(commandPayload.AsSpan(8, 4), 777);
        BinaryPrimitives.WriteInt32LittleEndian(commandPayload.AsSpan(12, 4), 0);
        var command = Packet(34, commandPayload);
        var parsed = DesktopWireFramer.DecodeSimpleCommand(command);
        Check(parsed == new DesktopCommandInfo(34, 17, -4, 777, 0),
              "simple command int32 little endian fields");
        Check(f.Feed(command)[0] == new DesktopFrameInfo(24, 34, 16),
              "simple command frame metadata");

        // TCP is a byte stream: fragmented frames and multiple frames per chunk.
        f.Reset();
        var fragments = new List<DesktopFrameInfo>();
        foreach (byte b in command)
            fragments.AddRange(f.Feed(new[] { b }));
        Check(fragments.Count == 1 && fragments[0].LengthBytes == 24 &&
              f.BufferedBytes == 0, "one-byte TCP fragmentation");

        var concatenated = minimum.Concat(command).Concat(minimum).ToArray();
        var many = f.Feed(concatenated);
        Check(many.Count == 3 && many[0].Opcode == 23 &&
              many[1].Opcode == 34 && many[2].Opcode == 23,
              "coalesced frames in one TCP read");

        // The parser handles the old code's socket buffer limit (8192 bytes).
        var full = Packet(100, new byte[DesktopWireFramer.MaxFrameBytes - 8]);
        Check(f.Feed(full).Single() == new DesktopFrameInfo(8192, 100, 8184),
              "maximum permitted frame");

        // Handle incomplete header and incomplete payload across multiple calls.
        f.Reset();
        Check(f.Feed(command.AsSpan(0, 5)).Count == 0 && f.BufferedBytes == 5,
              "partial header buffering");
        Check(f.Feed(command.AsSpan(5, 10)).Count == 0 && f.BufferedBytes == 15,
              "partial body buffering");
        Check(f.Feed(command.AsSpan(15)).Count == 1 && f.BufferedBytes == 0,
              "body completes the buffered command");

        // Invalid packet sizes must reject without emitting anything or leaving
        // contaminated partial buffers. Unknown/negative opcodes are metadata
        // only and are not dispatched.
        foreach (int invalid in new[] { -1, 0, 1, 7, 8193, int.MaxValue })
        {
            var bad = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(0, 4), invalid);
            Throws<FormatException>(() => f.Feed(bad),
                "invalid header byte count: " + invalid);
            Check(f.BufferedBytes == 0, "reset after invalid frame size");
        }

        // Follow-up valid packet remains parseable after malicious length.
        Check(f.Feed(minimum).Count == 1, "recovery after rejected header");
        Throws<FormatException>(() => DesktopWireFramer.DecodeSimpleCommand(minimum),
            "reject non-command frame length");
        var lying = (byte[])command.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(lying.AsSpan(0, 4), 23);
        Throws<FormatException>(() => DesktopWireFramer.DecodeSimpleCommand(lying),
            "reject command length mismatch");

        Throws<ArgumentOutOfRangeException>(() =>
            f.Feed(new byte[DesktopWireFramer.MaxInputChunkBytes + 1]),
            "reject oversized input chunk");

        // The preflight never emits payload bytes, credentials or player state.
        Check(typeof(DesktopFrameInfo).GetProperties().All(p =>
              p.PropertyType == typeof(int)), "metadata-only frame result");

        // TRANS_PLAYPOS is size(4), opcode(4), area(4), xyz(12), Hp[2](4).
        var position = Position(7, 256 * 5, -256 * 3, 128, 50, 100);
        var movement = DesktopPositionDecoder.Decode(position);
        Check(movement.RawAreaCode == 7 && movement.AreaCode == 7,
              "movement field area");
        Check(movement.RawX == 1280 && movement.RawY == -768 &&
              movement.RawZ == 128, "raw world position fixed-point");
        Check(movement.X == 5 && movement.Y == -3 && movement.Z == 0.5,
              "convert original FLOATNS shift 8");
        Check(movement.CurrentHp == 50 && movement.MaximumHp == 100,
              "original short Hp fields");
        Check(movement.BasicHpPlausibility, "basic health plausibility on sample");
        var view = DesktopPositionView.FromObservation(movement);
        Check(view.AreaCode == 7 && view.X == 5 &&
              view.WorldAccessGranted == false,
              "public position view never grants world access");
        Check(f.Feed(position).Single() ==
              new DesktopFrameInfo(28, DesktopPositionDecoder.PositionOpcode, 20),
              "position combines with generic TCP framing");

        var outsideStage = DesktopPositionDecoder.Decode(
            Position(uint.MaxValue, -1, -257, int.MaxValue, 0, 0));
        Check(outsideStage.AreaCode is null && !outsideStage.HasArea,
              "negative one area encodes absent map");
        Check(outsideStage.Y == -257.0 / 256.0 &&
              outsideStage.Z == (double)int.MaxValue / 256.0,
              "negative and extreme raw coordinates decode without overflow");
        var suspiciousHp = DesktopPositionDecoder.Decode(
            Position(0, 0, 0, 0, -1, 60));
        Check(!suspiciousHp.BasicHpPlausibility,
              "negative health flagged without mutating player");
        var tooHighHp = DesktopPositionDecoder.Decode(
            Position(1, 0, 0, 0, 201, 100));
        Check(!tooHighHp.BasicHpPlausibility,
              "implausible health flagged without server writes");

        var spoofedOpcode = (byte[])position.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(spoofedOpcode.AsSpan(4, 4), 34);
        Throws<FormatException>(() => DesktopPositionDecoder.Decode(spoofedOpcode),
              "wrong opcode is not a position frame");

        var spoofedLength = (byte[])position.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(spoofedLength.AsSpan(0, 4), 8192);
        Throws<FormatException>(() => DesktopPositionDecoder.Decode(spoofedLength),
              "wrong declared length is not a position frame");

        Throws<FormatException>(() => DesktopPositionDecoder.Decode(position.AsSpan(0, 27)),
              "truncated position frame rejected");

        Check(DesktopPositionDecoder.PositionFrameBytes == 28 &&
              DesktopPositionDecoder.PositionOpcode == 23,
              "native movement opcode and frame size");
        Check(DesktopPositionObservation.FixedPointShift == 8 &&
              DesktopPositionObservation.FixedPointFactor == 256,
              "native FLOATNS fixed-point shift");
        Check(typeof(DesktopPositionObservation).GetProperties()
              .All(p => p.Name != "Authenticated" && p.Name != "Authorized"),
              "movement observation cannot represent session authorization");

        Console.WriteLine($"PASS: {passed} Desktop protocol framing and position assertions.");
        Console.WriteLine("PASS: no network, no player credentials, no server modification.");
        Console.WriteLine("NOTE: packet opcode handlers, encryption and world state are NOT implemented.");
        return 0;
    }
}
