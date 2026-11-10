using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace StormPT.Gateway;

// Opaque bearer tokens are server-side only. This LAB store is ephemeral:
// a restart invalidates all sessions. Production requires durable distributed sessions.
internal sealed class LabSessions
{
    private const int MaxSessions = 2048;
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private sealed record Session(string Username, DateTimeOffset ExpiresAt);

    internal sealed record Ticket(string AccessToken, int ExpiresInSeconds);

    private static string Digest(string rawToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(rawToken)));

    internal bool TryIssue(string username, out Ticket? ticket)
    {
        ticket = null;
        if (!Regex.IsMatch(username, "^[A-Za-z0-9]{1,32}$"))
            return false;

        // Expired sessions are pruned upon issuance; exhaustion fails closed.
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _sessions)
            if (pair.Value.ExpiresAt <= now)
                _sessions.TryRemove(pair.Key, out _);

        if (_sessions.Count >= MaxSessions) return false;
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var entry = new Session(username, now.AddMinutes(15));
        if (!_sessions.TryAdd(Digest(raw), entry)) return false;
        ticket = new Ticket(raw, 15 * 60);
        return true;
    }

    internal bool TryRead(string? authorizationHeader, out string? username)
        => TryRead(authorizationHeader, out username, out _);

    internal bool TryRead(string? authorizationHeader, out string? username, out string? sessionKey)
    {
        username = null;
        sessionKey = null;
        if (authorizationHeader is null ||
            !authorizationHeader.StartsWith("Bearer ", StringComparison.Ordinal) ||
            authorizationHeader.Length != 50) // 7 prefix + 43 random base64url chars.
            return false;

        var token = authorizationHeader[7..];
        foreach (char c in token)
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
                return false;

        string key = Digest(token);
        if (!_sessions.TryGetValue(key, out var session)) return false;
        if (session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(key, out _);
            return false;
        }
        username = session.Username;
        sessionKey = key;
        return true;
    }

    internal bool Revoke(string? authorizationHeader)
    {
        if (!TryRead(authorizationHeader, out _)) return false;
        return _sessions.TryRemove(Digest(authorizationHeader![7..]), out _);
    }
}
