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

        // NFMsgBase.proto Position: float x=1, y=2, z=3 (wire type fixed32).
        // This is numeric mapping only, not an authenticated movement command.
        var encoded = MobilePositionProtoCodec.EncodeFromDesktop(movement);
        Check(encoded.Length == MobilePositionProtoCodec.ProtoPayloadBytes &&
              encoded[0] == 0x0D && encoded[5] == 0x15 && encoded[10] == 0x1D,
              "three required protobuf Position fields use fixed32 wire type");

        var mobile = MobilePositionProtoCodec.Decode(encoded);
        Check(mobile.X == 5.0f && mobile.Y == -3.0f && mobile.Z == 0.5f,
              "native fixed-point values map to Mobile float32 xyz");

        // Protobuf fields are unordered: reordered fields must decode identically.
        var permuted = encoded.AsSpan(10, 5).ToArray()
            .Concat(encoded.AsSpan(0, 5).ToArray())
            .Concat(encoded.AsSpan(5, 5).ToArray()).ToArray();
        Check(MobilePositionProtoCodec.Decode(permuted) == mobile,
              "protobuf Position field order independence");

        var fromFloat = MobilePositionProtoCodec.Encode(
            new MobilePositionSample(-1.25f, 2.5f, 0));
        Check(MobilePositionProtoCodec.Decode(fromFloat) ==
              new MobilePositionSample(-1.25f, 2.5f, 0f),
              "Mobile float32 encode decode");

        Throws<FormatException>(() => MobilePositionProtoCodec.Decode(encoded.AsSpan(0, 14)),
              "truncated Mobile Position rejected");

        var duplicatedField = (byte[])encoded.Clone();
        duplicatedField[5] = 0x0D;
        Throws<FormatException>(() => MobilePositionProtoCodec.Decode(duplicatedField),
              "duplicate protobuf field rejected");

        var wrongWireType = (byte[])encoded.Clone();
        wrongWireType[10] = 0x1A;
        Throws<FormatException>(() => MobilePositionProtoCodec.Decode(wrongWireType),
              "incorrect protobuf float wire type rejected");

        var infinity = (byte[])encoded.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(infinity.AsSpan(1, 4),
            BitConverter.SingleToInt32Bits(float.PositiveInfinity));
        Throws<FormatException>(() => MobilePositionProtoCodec.Decode(infinity),
              "infinite Mobile coordinates rejected");

        var nan = (byte[])encoded.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(nan.AsSpan(6, 4),
            BitConverter.SingleToInt32Bits(float.NaN));
        Throws<FormatException>(() => MobilePositionProtoCodec.Decode(nan),
              "NaN Mobile coordinates rejected");

        Throws<FormatException>(() =>
            MobilePositionProtoCodec.Encode(new MobilePositionSample(0, float.NaN, 1)),
            "NaN coordinates cannot be encoded");

        // At large world coordinates, float32 may lose a fraction of the native
        // fixed-point resolution. Test checks a bounded conversion error only,
        // not in-game coordinate calibration.
        var largeCoord = DesktopPositionDecoder.Decode(
            Position(0, int.MaxValue, int.MinValue, 256, 1, 1));
        var largeRoundTrip = MobilePositionProtoCodec.Decode(
            MobilePositionProtoCodec.EncodeFromDesktop(largeCoord));
        Check(Math.Abs((double)largeRoundTrip.X - largeCoord.X) <= 0.5 &&
              Math.Abs((double)largeRoundTrip.Y - largeCoord.Y) <= 0.5,
              "float32 rounding at extreme fixed-point values remains bounded");

        // Native dwObjectSerial is a server-generated transient entity serial,
        // not a permanent character ID. NFMsg.Ident(svrid,index) is a separate
        // namespace: only server-confirmed events can establish an association.
        var boot1 = Guid.NewGuid();
        var boot2 = Guid.NewGuid();
        var worldIds = new LabIdentityCrosswalk(boot1);
        var alice = new LabEntityBinding(
            "TEST1", "LabKnight", "session-A",
            new NativeRuntimeEntity(boot1, 0x10002000),
            new MobileRuntimeEntity(1, 101));
        var bob = new LabEntityBinding(
            "TEST2", "LabArcher", "session-B",
            new NativeRuntimeEntity(boot1, 0x10002001),
            new MobileRuntimeEntity(1, 102));

        Check(worldIds.BoundCount == 0, "crosswalk starts empty");
        Check(worldIds.BindVerified(alice) == BindOutcome.Created,
              "trusted server mapping created");
        Check(worldIds.BindVerified(alice) == BindOutcome.AlreadyBound,
              "repeated same trusted mapping is idempotent");
        Check(worldIds.BindVerified(bob) == BindOutcome.Created,
              "second unrelated player maps independently");
        Check(worldIds.BoundCount == 2, "two distinct verified sessions bound");

        Check(worldIds.TryResolveNative(boot1, "session-A", 0x10002000, out var aliceMobile) &&
              aliceMobile == new MobileRuntimeEntity(1, 101),
              "native serial resolves with authentic session and server boot");
        Check(worldIds.TryResolveMobile(boot1, "session-B",
              new MobileRuntimeEntity(1, 102), out uint bobNative) &&
              bobNative == 0x10002001,
              "mobile entity resolves to native serial with owned session");

        Check(!worldIds.TryResolveNative(boot1, "session-B", 0x10002000, out _),
              "another authenticated session cannot resolve Alice serial");
        Check(!worldIds.TryResolveMobile(boot1, "session-A",
              new MobileRuntimeEntity(1, 102), out _),
              "another session cannot resolve Bob mobile Ident");
        Check(!worldIds.TryResolveNative(boot2, "session-A", 0x10002000, out _),
              "wrong boot epoch cannot resolve transient ID");
        Check(!worldIds.TryResolveNative(boot1, "", 0x10002000, out _),
              "missing session cannot resolve player ID");

        var duplicateNative = bob with { Native = alice.Native, SessionKey = "session-C" };
        Check(worldIds.BindVerified(duplicateNative) == BindOutcome.Conflict,
              "native serial collision rejected");
        var duplicateMobile = bob with {
            Native = new NativeRuntimeEntity(boot1, 0x10002004),
            Mobile = alice.Mobile, SessionKey = "session-C"
        };
        Check(worldIds.BindVerified(duplicateMobile) == BindOutcome.Conflict,
              "Mobile Ident collision rejected");
        var duplicateSession = bob with {
            Native = new NativeRuntimeEntity(boot1, 0x10002004),
            Mobile = new MobileRuntimeEntity(1, 106),
            SessionKey = "session-A"
        };
        Check(worldIds.BindVerified(duplicateSession) == BindOutcome.Conflict,
              "one session cannot control two runtime identities");
        Check(worldIds.BoundCount == 2, "collisions do not mutate valid bindings");

        Check(worldIds.BindVerified(alice with {
            Native = new NativeRuntimeEntity(boot2, 0x10002000),
            SessionKey = "new-boot"
        }) == BindOutcome.Invalid, "stale/future boot binding rejected");
        Check(worldIds.BindVerified(alice with {
            Native = new NativeRuntimeEntity(boot1, 0)
        }) == BindOutcome.Invalid, "zero native serial rejected");
        Check(worldIds.BindVerified(alice with {
            Mobile = new MobileRuntimeEntity(1, 0)
        }) == BindOutcome.Invalid, "invalid mobile index rejected");
        Check(worldIds.BindVerified(alice with { Account = "" }) == BindOutcome.Invalid,
              "unverified account cannot be bound");

        Check(worldIds.ForgetVerifiedSession("session-A"),
              "logout clears an identity binding");
        Check(!worldIds.TryResolveNative(boot1, "session-A", 0x10002000, out _),
              "removed session loses access immediately");
        Check(worldIds.BoundCount == 1, "session revoke only clears its mapping");
        Check(!worldIds.ForgetVerifiedSession("session-A"),
              "duplicate logout does not corrupt registry");

        // After server restart, the *same numeric serial* may reappear.
        worldIds.ResetForNativeServerRestart(boot2);
        Check(worldIds.BoundCount == 0, "server restart invalidates every mapping");
        Check(!worldIds.TryResolveNative(boot1, "session-B", 0x10002001, out _),
              "prior epoch mapping cannot be reused");
        Check(worldIds.BindVerified(new LabEntityBinding(
                "TEST1", "LabKnight", "new-session",
                new NativeRuntimeEntity(boot2, 0x10002000),
                new MobileRuntimeEntity(9, 501))) == BindOutcome.Created,
              "recycled native serial requires new verified identity and epoch");
        Check(!worldIds.TryResolveMobile(boot2, "session-A",
              new MobileRuntimeEntity(9, 501), out _),
              "expired session cannot take over recycled identity");
        Throws<ArgumentException>(() => worldIds.ResetForNativeServerRestart(boot2),
              "restart cannot reuse the same epoch");
        Throws<ArgumentException>(() => new LabIdentityCrosswalk(Guid.Empty),
              "server epoch must not be empty");

        Console.WriteLine($"PASS: {passed} Desktop protocol, position and runtime identity assertions.");
        Console.WriteLine("PASS: no network, no player credentials, no server modification.");
        Console.WriteLine("NOTE: packet opcode handlers, encryption and world state are NOT implemented.");
        return 0;
    }
}
