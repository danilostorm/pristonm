using System.Buffers.Binary;

namespace StormPT.MobileLab;

/// <summary>
/// Offline, metadata-only preflight for the 8-byte packet envelope found in
/// StormPT's smwsock.cpp. This is NOT a network client, opcode dispatcher,
/// decrypter, serializer or authorization mechanism.
///
/// Envelope observed in the old LAB code snapshot:
/// int32 little-endian frame byte count (INCLUDING 8-byte header),
/// int32 little-endian packet opcode; valid length 8..8192.
/// Both signed int32 fields are read as stored. Actual gameplay can use
/// further encoding / context beyond this preflight.
/// </summary>
public sealed record DesktopFrameInfo(int LengthBytes, int Opcode, int PayloadBytes);

public sealed record DesktopCommandInfo(
    int Opcode, int LParam, int WParam, int SParam, int EParam);

public sealed class DesktopWireFramer
{
    public const int HeaderBytes = 8;
    public const int MaxFrameBytes = 8192;
    public const int SimpleCommandBytes = 24;
    public const int MaxInputChunkBytes = 64 * 1024;
    public const int MaxFramesPerFeed = MaxInputChunkBytes / HeaderBytes;

    private readonly byte[] _current = new byte[MaxFrameBytes];
    private int _received;
    private int _target;

    public int BufferedBytes => _received;

    public void Reset()
    {
        Array.Clear(_current);
        _received = 0;
        _target = 0;
    }

    public IReadOnlyList<DesktopFrameInfo> Feed(ReadOnlySpan<byte> input)
    {
        if (input.Length > MaxInputChunkBytes)
            throw new ArgumentOutOfRangeException(nameof(input), "Chunk exceeds the LAB limit.");

        var output = new List<DesktopFrameInfo>();
        int offset = 0;

        try
        {
            while (offset < input.Length)
            {
                if (_received < HeaderBytes)
                {
                    int takeHeader = Math.Min(HeaderBytes - _received, input.Length - offset);
                    input.Slice(offset, takeHeader).CopyTo(_current.AsSpan(_received));
                    _received += takeHeader;
                    offset += takeHeader;
                    if (_received < HeaderBytes)
                        break;

                    int length = BinaryPrimitives.ReadInt32LittleEndian(_current.AsSpan(0, 4));
                    if (length < HeaderBytes || length > MaxFrameBytes)
                        throw new FormatException("Native frame declares an invalid packet length.");
                    _target = length;
                }

                int take = Math.Min(_target - _received, input.Length - offset);
                if (take > 0)
                {
                    input.Slice(offset, take).CopyTo(_current.AsSpan(_received));
                    _received += take;
                    offset += take;
                }

                if (_received == _target)
                {
                    if (output.Count >= MaxFramesPerFeed)
                        throw new FormatException("Too many frames in a single chunk.");

                    // Do not expose or log game data: this is an envelope scanner.
                    int opcode = BinaryPrimitives.ReadInt32LittleEndian(_current.AsSpan(4, 4));
                    output.Add(new DesktopFrameInfo(_target, opcode, _target - HeaderBytes));
                    Array.Clear(_current, 0, _received);
                    _received = 0;
                    _target = 0;
                }
            }
            return output;
        }
        catch
        {
            Reset(); // Fail closed and discard any partial frame.
            throw;
        }
    }

    /// <summary>
    /// Structural decoder of smTRANS_COMMAND (6 x 32-bit little-endian ints).
    /// Call only when a higher-level, authenticated decoder has established
    /// the opcode denotes this command. No opcode whitelist is assumed.
    /// </summary>
    public static DesktopCommandInfo DecodeSimpleCommand(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SimpleCommandBytes ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(0, 4)) != SimpleCommandBytes)
            throw new FormatException("Expected a complete 24-byte smTRANS_COMMAND.");

        return new DesktopCommandInfo(
            BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(4, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(8, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(12, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(16, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(20, 4)));
    }
}
