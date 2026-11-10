using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using StormPT.MobileLab;

namespace StormPT.Gateway;

/// <summary>
/// A signed, loopback-only LAB event sink for FUTURE server-side instrumentation.
///
/// This does not read game memory, run in Server33, authenticate players,
/// or allow world entry. Only an external, trusted sidecar could emit events.
/// Real production work needs mTLS, persistence and an independently audited
/// server-side instrumentation point. Do NOT expose through an HTTP proxy.
/// </summary>
internal sealed record LabWorldEvent(
    string? EventType,
    Guid ServerEpoch,
    long Sequence,
    string? ServerSession,
    string? Account,
    string? Character,
    uint NativeObjectSerial,
    long MobileServerId,
    long MobileIndex);

internal readonly record struct WorldEventOutcome(int Status, string Code)
{
    internal static WorldEventOutcome Ok => new(204, "applied");
}

internal sealed class LabWorldEventReceiver
{
    private const int MaxBodyBytes = 4096;
    private const int NonceLimit = 4096;
    private const int MaxClockSkewSeconds = 20;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly byte[]? _secret;
    private readonly object _gate = new();
    private readonly Dictionary<string, long> _recentNonces = new(StringComparer.Ordinal);
    private LabIdentityCrosswalk? _identities;
    private Guid _epoch;
    private long _lastSequence;

    internal bool Enabled => _secret is not null;

    public LabWorldEventReceiver()
    {
        if (Environment.GetEnvironmentVariable("STORMPT_LAB_ENABLE_WORLD_EVENTS") != "1")
            return;

        var configured = Environment.GetEnvironmentVariable("STORMPT_LAB_WORLD_EVENT_KEY_B64");
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("World events enabled without signing key.");
        try
        {
            var raw = Convert.FromBase64String(configured);
            if (raw.Length is < 32 or > 64)
                throw new InvalidOperationException("Signing key length is invalid.");
            _secret = raw;
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("World event key encoding invalid.");
        }
    }

    internal WorldEventOutcome VerifyAndApply(
        string? timestamp, string? nonce, string? macHex, byte[] body)
    {
        if (_secret is null) return new(503, "disabled");
        if (body is null || body.Length is < 2 or > MaxBodyBytes)
            return new(400, "body_invalid");

        if (!long.TryParse(timestamp, NumberStyles.None,
                CultureInfo.InvariantCulture, out var unixSeconds))
            return new(401, "not_authenticated");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (unixSeconds < now - MaxClockSkewSeconds ||
            unixSeconds > now + MaxClockSkewSeconds)
            return new(401, "not_authenticated");

        if (nonce is null || nonce.Length != 32 ||
            !nonce.All(Uri.IsHexDigit) ||
            macHex is null || macHex.Length != 64 ||
            !macHex.All(Uri.IsHexDigit))
            return new(401, "not_authenticated");

        // HMAC-SHA256 over canonical ASCII prefix and the EXACT UTF-8 bytes,
        // avoiding ambiguous reserialization of JSON.
        var prefix = Encoding.ASCII.GetBytes(timestamp + "\n" + nonce + "\n");
        var signed = new byte[prefix.Length + body.Length];
        Buffer.BlockCopy(prefix, 0, signed, 0, prefix.Length);
        Buffer.BlockCopy(body, 0, signed, prefix.Length, body.Length);
        var expected = HMACSHA256.HashData(_secret, signed);
        if (!Convert.TryFromHexString(macHex, new byte[32], out int written) ||
            written != 32)
            return new(401, "not_authenticated");

        var provided = Convert.FromHexString(macHex);
        if (!CryptographicOperations.FixedTimeEquals(expected, provided))
            return new(401, "not_authenticated");

        LabWorldEvent? evt;
        try
        {
            evt = JsonSerializer.Deserialize<LabWorldEvent>(
                StrictUtf8.GetString(body), JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException)
        {
            return new(400, "invalid_event");
        }
        if (evt is null || evt.Sequence <= 0 || evt.ServerEpoch == Guid.Empty)
            return new(400, "invalid_event");

        lock (_gate)
        {
            // Monotonic across boot events in this one gateway process.
            // A real server adapter must persist/synchronize its counter;
            // restarting the gateway loses this ephemeral state.
            foreach (var key in _recentNonces.Where(x => x.Value < now - 60)
                                              .Select(x => x.Key).ToArray())
                _recentNonces.Remove(key);

            if (_recentNonces.ContainsKey(nonce))
                return new(409, "replay");
            if (_recentNonces.Count >= NonceLimit)
                return new(503, "nonce_capacity");
            if (evt.Sequence <= _lastSequence)
                return new(409, "out_of_order");

            WorldEventOutcome result;
            switch (evt.EventType)
            {
                case "boot":
                    if (_identities is not null && evt.ServerEpoch == _epoch)
                        return new(409, "same_epoch");
                    // Only a signed, strictly increasing boot can reset state.
                    _identities = new LabIdentityCrosswalk(evt.ServerEpoch);
                    _epoch = evt.ServerEpoch;
                    result = WorldEventOutcome.Ok;
                    break;

                case "spawn":
                case "despawn":
                    if (_identities is null || evt.ServerEpoch != _epoch)
                        return new(409, "unknown_epoch");
                    if (!ValidIdentity(evt))
                        return new(422, "invalid_identity");

                    var binding = new LabEntityBinding(
                        evt.Account!, evt.Character!, evt.ServerSession!,
                        new NativeRuntimeEntity(evt.ServerEpoch, evt.NativeObjectSerial),
                        new MobileRuntimeEntity(evt.MobileServerId, evt.MobileIndex));

                    if (evt.EventType == "spawn")
                    {
                        var outcome = _identities.BindVerified(binding);
                        result = outcome switch
                        {
                            BindOutcome.Created or BindOutcome.AlreadyBound =>
                                WorldEventOutcome.Ok,
                            BindOutcome.CapacityExceeded => new(503, "entity_capacity"),
                            BindOutcome.Invalid => new(422, "invalid_identity"),
                            _ => new(409, "binding_conflict")
                        };
                    }
                    else
                    {
                        result = _identities.ForgetExactBinding(binding)
                            ? WorldEventOutcome.Ok
                            : new(409, "binding_mismatch");
                    }
                    break;

                default:
                    return new(422, "unknown_event");
            }

            if (result.Status == 204)
            {
                _lastSequence = evt.Sequence;
                _recentNonces.Add(nonce, now);
            }
            return result;
        }
    }

    private static bool ValidIdentity(LabWorldEvent evt)
    {
        // Keep these as server-side statements; never accept browser-supplied
        // sessions, native serials or Mobile Idents as evidence.
        return evt.Account is { Length: >= 1 and <= 32 } &&
               Regex.IsMatch(evt.Account, "^[A-Za-z0-9]{1,32}$") &&
               evt.Character is { Length: >= 1 and <= 64 } &&
               !evt.Character.Any(char.IsControl) &&
               evt.ServerSession is { Length: >= 1 and <= 128 } &&
               Regex.IsMatch(evt.ServerSession, "^[A-Za-z0-9_-]{1,128}$") &&
               evt.NativeObjectSerial != 0 &&
               evt.MobileServerId >= 0 && evt.MobileIndex > 0;
    }

    // Local-only status for automated tests; not exposed by any HTTP endpoint.
    internal int BoundCount
    {
        get { lock (_gate) return _identities?.BoundCount ?? 0; }
    }
}
