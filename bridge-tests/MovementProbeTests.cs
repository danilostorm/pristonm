using StormPT.MobileLab;

internal static class MovementProbeTests
{
    private static int _passed;

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("FAILED: " + message);
        _passed++;
    }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (FormatException) { _passed++; return; }
        throw new Exception("FAILED, expected FormatException: " + message);
    }

    private static byte[] Varint(ulong number)
    {
        var bytes = new List<byte>();
        while (number > 0x7F)
        {
            bytes.Add((byte)((number & 0x7F) | 0x80));
            number >>= 7;
        }
        bytes.Add((byte)number);
        return bytes.ToArray();
    }

    private static byte[] Nested(byte tag, byte[] payload) =>
        new byte[] { tag }.Concat(Varint((ulong)payload.Length))
                          .Concat(payload).ToArray();

    private static byte[] MobileIdent(ulong serverId, ulong index) =>
        new byte[] { 0x08 }.Concat(Varint(serverId))
            .Concat(new byte[] { 0x10 }).Concat(Varint(index)).ToArray();

    private static byte[] Move(ulong serverId, ulong index, ulong moveType,
        byte[]? position = null)
    {
        var bytes = Nested(0x0A, MobileIdent(serverId, index))
            .Concat(new byte[] { 0x10 }).Concat(Varint(moveType)).ToArray();
        if (position is not null)
        {
            bytes = bytes.Concat(Nested(0x1A, position)).ToArray();
            bytes = bytes.Concat(Nested(0x22, position)).ToArray();
        }
        return bytes;
    }

    internal static int Run()
    {
        byte[] position = MobilePositionProtoCodec.Encode(
            new MobilePositionSample(5.0f, -3.0f, 0.5f));
        byte[] payload = Move(1, 101, 2, position);
        var decoded = MobileMovementProtoProbe.Decode(payload);
        Assert(decoded.Mover == new MobileRuntimeEntity(1, 101),
            "decode NFMsg Ident with two required int64 components");
        Assert(decoded.MoveType == 2, "decode movement type");
        Assert(decoded.TargetPositions.Count == 1 &&
               decoded.TargetPositions[0] == new MobilePositionSample(5f, -3f, 0.5f),
            "decode target positions");
        Assert(decoded.SourcePositions.Count == 1 &&
               decoded.SourcePositions[0] == decoded.TargetPositions[0],
            "decode source positions");

        var epoch = Guid.NewGuid();
        var crosswalk = new LabIdentityCrosswalk(epoch);
        var trusted = new LabEntityBinding(
            "TEST1", "LabKnight", "server-verified-session",
            new NativeRuntimeEntity(epoch, 0x10002000),
            new MobileRuntimeEntity(1, 101));
        Assert(crosswalk.BindVerified(trusted) == BindOutcome.Created,
            "trusted runtime identity can be registered by server adapter");
        Assert(MobileMovementProtoProbe.IsBoundToVerifiedSession(
                decoded, crosswalk, epoch, "server-verified-session", out var serial) &&
               serial == 0x10002000,
            "movement actor maps to native player only for verified session");
        Assert(!MobileMovementProtoProbe.IsBoundToVerifiedSession(
                decoded, crosswalk, epoch, "different-session", out _),
            "mobile movement rejects a session that does not own actor");
        Assert(!MobileMovementProtoProbe.IsBoundToVerifiedSession(
                decoded, crosswalk, Guid.NewGuid(),
                "server-verified-session", out _),
            "mobile movement rejects stale/restarted native server");
        var impersonation = MobileMovementProtoProbe.Decode(Move(1, 999, 2, position));
        Assert(!MobileMovementProtoProbe.IsBoundToVerifiedSession(
                impersonation, crosswalk, epoch, "server-verified-session", out _),
            "client cannot claim a different mobile Ident");
        Assert(crosswalk.ForgetVerifiedSession("server-verified-session"),
            "server disconnect releases player identity");
        Assert(!MobileMovementProtoProbe.IsBoundToVerifiedSession(
                decoded, crosswalk, epoch, "server-verified-session", out _),
            "post-disconnect movement is not bound");

        var noPosition = MobileMovementProtoProbe.Decode(Move(1, 101, 1));
        Assert(noPosition.TargetPositions.Count == 0 &&
               noPosition.SourcePositions.Count == 0,
            "protobuf repeated positions may be omitted");
        Reject(() => MobileMovementProtoProbe.Decode(Array.Empty<byte>()),
            "empty movement");
        Reject(() => MobileMovementProtoProbe.Decode(new byte[4097]),
            "oversized movement");
        Reject(() => MobileMovementProtoProbe.Decode(new byte[] { 0x10, 0x01 }),
            "missing mover");
        Reject(() => MobileMovementProtoProbe.Decode(
            Nested(0x0A, MobileIdent(1, 101))),
            "missing move type");
        Reject(() => MobileMovementProtoProbe.Decode(
            Nested(0x0A, MobileIdent(1, 0)).Concat(
                new byte[] { 0x10, 0x01 }).ToArray()),
            "zero mobile entity index");
        Reject(() => MobileMovementProtoProbe.Decode(
            Nested(0x0A, new byte[] { 0x08, 0x01 }).Concat(
                new byte[] { 0x10, 0x01 }).ToArray()),
            "missing mobile identity index");
        Reject(() => MobileMovementProtoProbe.Decode(
            Nested(0x0A, new byte[] { 0x08, 0x01, 0x08, 0x01, 0x10, 0x65 })
                .Concat(new byte[] { 0x10, 0x01 }).ToArray()),
            "duplicate mobile identity server number");
        Reject(() => MobileMovementProtoProbe.Decode(
            Move(1, 101, 2).Concat(new byte[] { 0x10, 0x02 }).ToArray()),
            "duplicate movement type");
        Reject(() => MobileMovementProtoProbe.Decode(
            Move(1, 101, 2).Concat(new byte[] { 0x28, 0x01 }).ToArray()),
            "unexpected future or attacker-defined field");
        Reject(() => MobileMovementProtoProbe.Decode(
            Move(1, 101, 2).Concat(new byte[] { 0x1A, 0x12, 0x00 }).ToArray()),
            "truncated nested Position");
        Reject(() => MobileMovementProtoProbe.Decode(
            Move(1, 101, 2).Concat(Nested(0x1A,
                new byte[] { 0x0D, 0, 0, 0, 0 })).ToArray()),
            "missing nested y and z");
        Reject(() => MobileMovementProtoProbe.Decode(
            Move(1, 101, 2).Concat(new byte[] { 0xFF }).ToArray()),
            "truncated protobuf tag varint");
        Reject(() => MobileMovementProtoProbe.Decode(
            Move(ulong.MaxValue, 101, 1)),
            "out-of-range signed int64 svrid");
        Reject(() => MobileMovementProtoProbe.Decode(
            Move(1, 101, (ulong)int.MaxValue + 1)),
            "out-of-range int32 moveType");
        var lots = Move(1, 101, 1);
        for (int i = 0; i < 17; i++)
            lots = lots.Concat(Nested(0x1A, position)).ToArray();
        byte[] tooMany = lots;
        Reject(() => MobileMovementProtoProbe.Decode(tooMany),
            "excessive target positions");

        Console.WriteLine($"PASS: {_passed} NFMsg movement/identity offline assertions.");
        Console.WriteLine("PASS: no network requests, no player data, no world mutation.");
        return _passed;
    }
}
