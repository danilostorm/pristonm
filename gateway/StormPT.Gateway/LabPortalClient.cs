using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace StormPT.Gateway;

internal enum LabAuthResult { Accepted, Denied, Unavailable }
internal enum LabProfileStatus { Ok, NotFound, Unavailable }

internal sealed record PublicCharacter(string Name, int ClassCode, string ClassName, int Level);
internal sealed record PublicProfile(string Username, int CharacterCount, IReadOnlyList<PublicCharacter> Characters);
internal sealed record LabProfileResult(LabProfileStatus Status, PublicProfile? Profile);

/// <summary>
/// Adapter for the EXISTING StormPT portal API, not for game packets.
/// Disabled unless explicitly enabled. HTTPS required except loopback.
/// Never bypass TLS validation, never follow redirects, never touch SQL directly.
/// </summary>
internal sealed class LabPortalClient : IDisposable
{
    private readonly HttpClient? _client;
    private readonly Uri? _origin;
    internal bool Enabled => _client is not null;

    internal LabPortalClient()
    {
        if (Environment.GetEnvironmentVariable("STORMPT_GATEWAY_ENABLE_LAB_AUTH") != "1")
            return;
        var origin = Environment.GetEnvironmentVariable("STORMPT_LAB_PORTAL_ORIGIN");
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/" ||
            (uri.Scheme != Uri.UriSchemeHttps &&
                !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            throw new InvalidOperationException(
                "LAB portal must be an HTTPS origin (HTTP only for loopback).");

        _origin = uri;
        _client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false
        })
        {
            Timeout = TimeSpan.FromSeconds(6),
            MaxResponseContentBufferSize = 64 * 1024
        };
    }

    internal async Task<LabAuthResult> VerifyAsync(
        string username, string password, CancellationToken cancellation)
    {
        if (_client is null || _origin is null) return LabAuthResult.Unavailable;
        try
        {
            using var response = await _client.PostAsJsonAsync(
                new Uri(_origin, "/api/login"), new { username, password }, cancellation);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return LabAuthResult.Denied;
            if (response.StatusCode != HttpStatusCode.OK)
                return LabAuthResult.Unavailable;

            using var json = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellation));
            var body = json.RootElement;
            if (!TryGet(body, "authenticated", out var yes) ||
                yes.ValueKind != JsonValueKind.True ||
                !TryGet(body, "username", out var name) ||
                name.ValueKind != JsonValueKind.String ||
                !string.Equals(name.GetString(), username, StringComparison.OrdinalIgnoreCase))
                return LabAuthResult.Denied;
            return LabAuthResult.Accepted;
        }
        catch (HttpRequestException) { return LabAuthResult.Unavailable; }
        catch (TaskCanceledException) { return LabAuthResult.Unavailable; }
        catch (JsonException) { return LabAuthResult.Unavailable; }
        catch (InvalidOperationException) { return LabAuthResult.Unavailable; }
    }

    internal async Task<LabProfileResult> GetProfileAsync(
        string authenticatedUsername, CancellationToken cancellation)
    {
        if (_client is null || _origin is null)
            return new LabProfileResult(LabProfileStatus.Unavailable, null);
        try
        {
            // The existing portal's /api/profile is NOT authenticated.
            // Access to this call MUST be mediated by a verified gateway session.
            using var response = await _client.PostAsJsonAsync(
                new Uri(_origin, "/api/profile"),
                new { username = authenticatedUsername }, cancellation);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new LabProfileResult(LabProfileStatus.NotFound, null);
            if (response.StatusCode != HttpStatusCode.OK)
                return new LabProfileResult(LabProfileStatus.Unavailable, null);

            using var json = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellation));
            var body = json.RootElement;
            if (!TryGet(body, "username", out var name) ||
                name.ValueKind != JsonValueKind.String ||
                !string.Equals(name.GetString(), authenticatedUsername,
                    StringComparison.OrdinalIgnoreCase) ||
                !TryGet(body, "characters", out var characters) ||
                characters.ValueKind != JsonValueKind.Array ||
                characters.GetArrayLength() > 12)
                return new LabProfileResult(LabProfileStatus.Unavailable, null);

            var output = new List<PublicCharacter>();
            foreach (var character in characters.EnumerateArray())
            {
                if (character.ValueKind != JsonValueKind.Object ||
                    !TryGet(character, "name", out var charName) ||
                    !TryGet(character, "className", out var className) ||
                    !TryGet(character, "classCode", out var classCode) ||
                    !TryGet(character, "level", out var level) ||
                    charName.ValueKind != JsonValueKind.String ||
                    className.ValueKind != JsonValueKind.String ||
                    !classCode.TryGetInt32(out int classId) ||
                    !level.TryGetInt32(out int charLevel))
                    return new LabProfileResult(LabProfileStatus.Unavailable, null);
                var cname = charName.GetString() ?? "";
                var klass = className.GetString() ?? "";
                if (cname.Length == 0 || cname.Length > 64 || klass.Length > 64 ||
                    charLevel is < 0 or > 500 || classId is < 0 or > 100)
                    return new LabProfileResult(LabProfileStatus.Unavailable, null);
                output.Add(new PublicCharacter(cname, classId, klass, charLevel));
            }
            var profile = new PublicProfile(authenticatedUsername, output.Count, output);
            return new LabProfileResult(LabProfileStatus.Ok, profile);
        }
        catch (HttpRequestException) { return new(LabProfileStatus.Unavailable, null); }
        catch (TaskCanceledException) { return new(LabProfileStatus.Unavailable, null); }
        catch (JsonException) { return new(LabProfileStatus.Unavailable, null); }
        catch (InvalidOperationException) { return new(LabProfileStatus.Unavailable, null); }
    }

    private static bool TryGet(JsonElement element, string key, out JsonElement found)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
                if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase))
                {
                    found = prop.Value;
                    return true;
                }
        }
        found = default;
        return false;
    }

    public void Dispose() => _client?.Dispose();
}
