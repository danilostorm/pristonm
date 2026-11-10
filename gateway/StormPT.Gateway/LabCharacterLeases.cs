namespace StormPT.Gateway;

/// <summary>
/// LAB-only in-memory exclusive lease for a character. This is not a game-world
/// login or distributed lock. Production game servers MUST enforce the same
/// exclusivity independently (or use a transactional shared lock).
/// </summary>
internal sealed class LabCharacterLeases
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(90);
    private const int MaxLeases = 2048;
    private readonly object _gate = new();
    private readonly Dictionary<string, Lease> _leases = new(StringComparer.Ordinal);
    private sealed record Lease(string SessionKey, DateTimeOffset ExpiresAt);

    internal enum AcquireResult { Reserved, Conflict, Full }

    private static string Key(string username, string character)
        => username.ToUpperInvariant() + "\0" + character.ToUpperInvariant();

    private void Prune(DateTimeOffset now)
    {
        foreach (var k in _leases.Where(x => x.Value.ExpiresAt <= now)
                                 .Select(x => x.Key).ToArray())
            _leases.Remove(k);
    }

    internal AcquireResult Reserve(string username, string character, string sessionKey)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            Prune(now);
            string key = Key(username, character);
            if (_leases.TryGetValue(key, out var existing) &&
                !string.Equals(existing.SessionKey, sessionKey, StringComparison.Ordinal))
                return AcquireResult.Conflict;

            // One character per bearer token. Other characters remain inaccessible
            // until the current reservation is released or expires.
            if (_leases.Any(x => x.Value.SessionKey == sessionKey && x.Key != key))
                return AcquireResult.Conflict;

            if (!_leases.ContainsKey(key) && _leases.Count >= MaxLeases)
                return AcquireResult.Full;

            _leases[key] = new Lease(sessionKey, now.Add(Lifetime));
            return AcquireResult.Reserved;
        }
    }

    internal bool Renew(string username, string character, string sessionKey)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            Prune(now);
            string key = Key(username, character);
            if (!_leases.TryGetValue(key, out var existing) ||
                existing.SessionKey != sessionKey)
                return false;
            _leases[key] = existing with { ExpiresAt = now.Add(Lifetime) };
            return true;
        }
    }

    internal bool Release(string username, string character, string sessionKey)
    {
        lock (_gate)
        {
            string key = Key(username, character);
            if (!_leases.TryGetValue(key, out var existing) ||
                existing.SessionKey != sessionKey)
                return false;
            _leases.Remove(key);
            return true;
        }
    }

    internal void ReleaseForSession(string sessionKey)
    {
        lock (_gate)
        {
            foreach (var k in _leases.Where(x => x.Value.SessionKey == sessionKey)
                                     .Select(x => x.Key).ToArray())
                _leases.Remove(k);
        }
    }
}
