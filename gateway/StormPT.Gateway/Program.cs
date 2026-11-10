using System.Net;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using StormPT.Gateway;
using StormPT.MobileLab;

var builder = WebApplication.CreateBuilder(args);

// HARD SAFETY BOUNDARY: local-only development service.
// Publishing through a reverse proxy requires a separate security review.
builder.WebHost.UseUrls("http://127.0.0.1:18878");
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 4096);
builder.Services.AddSingleton<LabSessions>();
builder.Services.AddSingleton<LabCharacterLeases>();
builder.Services.AddSingleton<LabWorldEventReceiver>();
builder.Services.AddSingleton<LabPortalClient>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 8,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next(context);
});
app.UseRateLimiter();

app.MapGet("/health", (LabPortalClient portal, LabWorldEventReceiver events) =>
    Results.Ok(new
    {
        service = "StormPT LAB integration gateway",
        status = "ok",
        realLoginConfigured = portal.Enabled,
        trustedWorldEventsConfigured = events.Enabled
    }));

// The event ingest endpoint is NOT a public API and must NEVER be forwarded
// by the Web or Mobile reverse proxy. HMAC is mandatory even for loopback.
// It only updates ephemeral ID observations; it never grants game access.
app.MapPost("/__lab/world-events", async (
    HttpContext ctx, LabWorldEventReceiver receiver) =>
{
    if (ctx.Connection.RemoteIpAddress is null ||
        !IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    if (!receiver.Enabled)
        return Results.Json(new { error = "LAB world events disabled." },
            statusCode: StatusCodes.Status503ServiceUnavailable);

    if (ctx.Request.ContentType is null ||
        !string.Equals(ctx.Request.ContentType, "application/json",
            StringComparison.OrdinalIgnoreCase) ||
        ctx.Request.ContentLength > 4096)
        return Results.BadRequest(new { error = "Invalid event content type or length." });

    using var bounded = new MemoryStream(capacity: 4096);
    // Kestrel also enforces a 4096-byte maximum for all request bodies.
    await ctx.Request.Body.CopyToAsync(bounded, ctx.RequestAborted);
    var outcome = receiver.VerifyAndApply(
        ctx.Request.Headers["X-StormPT-Lab-Time"].ToString(),
        ctx.Request.Headers["X-StormPT-Lab-Nonce"].ToString(),
        ctx.Request.Headers["X-StormPT-Lab-Signature"].ToString(),
        bounded.ToArray());

    return outcome.Status == 204
        ? Results.NoContent()
        : Results.Json(new { error = outcome.Status == 401
            ? "Invalid event authentication." : outcome.Code },
            statusCode: outcome.Status);
});

app.MapPost("/v1/sessions", async (
    LoginInput input, HttpContext ctx, LabPortalClient portal, LabSessions sessions) =>
{
    var username = input.Username?.Trim() ?? "";
    var password = input.Password ?? "";
    if (!Regex.IsMatch(username, "^[A-Za-z0-9]{1,32}$") ||
        password.Length is < 1 or > 32)
        return Results.BadRequest(new { error = "Invalid credentials format." });

    if (!portal.Enabled)
        return Results.Json(new { error = "LAB login backend not configured." },
            statusCode: StatusCodes.Status503ServiceUnavailable);

    var verified = await portal.VerifyAsync(username, password, ctx.RequestAborted);
    if (verified == LabAuthResult.Unavailable)
        return Results.Json(new { error = "LAB authentication unavailable." },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    if (verified != LabAuthResult.Accepted)
        return Results.Unauthorized();

    if (!sessions.TryIssue(username, out var ticket) || ticket is null)
        return Results.Json(new { error = "LAB session capacity exceeded." },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    return Results.Ok(new
    {
        access_token = ticket.AccessToken,
        token_type = "Bearer",
        expires_in = ticket.ExpiresInSeconds
    });
}).RequireRateLimiting("login");

app.MapGet("/v1/me", (HttpContext ctx, LabSessions sessions) =>
{
    if (!sessions.TryRead(ctx.Request.Headers.Authorization.ToString(), out var username))
        return Results.Unauthorized();
    return Results.Ok(new { username });
});

app.MapGet("/v1/characters", async (
    HttpContext ctx, LabSessions sessions, LabPortalClient portal) =>
{
    // Character ownership comes only from the validated server-side session.
    // Never accept an account name from a query string or from the request body.
    if (!sessions.TryRead(ctx.Request.Headers.Authorization.ToString(), out var username) ||
        username is null)
        return Results.Unauthorized();

    var result = await portal.GetProfileAsync(username, ctx.RequestAborted);
    return result.Status switch
    {
        LabProfileStatus.Ok when result.Profile is not null =>
            Results.Ok(result.Profile),
        LabProfileStatus.NotFound => Results.NotFound(),
        _ => Results.Json(new { error = "LAB character service unavailable." },
            statusCode: StatusCodes.Status503ServiceUnavailable)
    };
});

// First real adapter step: an authenticated Mobile Protobuf request can be
// inspected and mapped to a read-only gateway character-list response.
// This is NOT the original PTM TCP framing or a native Protobuf ACK serializer.
app.MapPost("/v1/mobile/role-list", async (
    HttpContext ctx, LabSessions sessions, LabPortalClient portal) =>
{
    if (!sessions.TryRead(ctx.Request.Headers.Authorization.ToString(), out var username) ||
        username is null)
        return Results.Unauthorized();

    if (ctx.Request.ContentType is null ||
        !ctx.Request.ContentType.StartsWith("application/x-protobuf", StringComparison.OrdinalIgnoreCase) ||
        ctx.Request.ContentLength > 4096)
        return Results.BadRequest(new { error = "Invalid Mobile message type or size." });

    using var body = new MemoryStream(capacity: 4096);
    await ctx.Request.Body.CopyToAsync(body, ctx.RequestAborted);
    var payload = body.ToArray();
    MobileRoleListProbeResult parsed;
    try
    {
        parsed = MobileRoleListProbe.Inspect(payload);
    }
    catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
    {
        return Results.BadRequest(new { error = "Malformed NFMsg.ReqRoleList." });
    }

    // Client-supplied account is only a claim, never proof of identity.
    if (!string.Equals(parsed.ClaimedAccount, username, StringComparison.OrdinalIgnoreCase))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var result = await portal.GetProfileAsync(username, ctx.RequestAborted);
    return result.Status switch
    {
        LabProfileStatus.Ok when result.Profile is not null => Results.Ok(new
        {
            protocol = "NFMsg.ReqRoleList",
            gameId = parsed.GameId,
            username,
            characters = result.Profile.Characters
        }),
        LabProfileStatus.NotFound => Results.NotFound(),
        _ => Results.Json(new { error = "LAB character service unavailable." },
            statusCode: StatusCodes.Status503ServiceUnavailable)
    };
});

// Character reservation is only a LAB-side concurrency check. It does NOT
// admit a player to the StormPT game world or update any inventory/save data.
app.MapPost("/v1/characters/reserve", async (
    CharacterReservationRequest input, HttpContext ctx, LabSessions sessions,
    LabPortalClient portal, LabCharacterLeases leases) =>
{
    if (!sessions.TryRead(ctx.Request.Headers.Authorization.ToString(),
            out var username, out var sessionKey) || username is null || sessionKey is null)
        return Results.Unauthorized();
    var requested = input.Name?.Trim() ?? "";
    if (requested.Length is < 1 or > 64 || requested.Any(char.IsControl))
        return Results.BadRequest(new { error = "Invalid character name." });

    // Ownership is checked against the authenticated account, never against
    // a client-provided account/character pairing.
    var result = await portal.GetProfileAsync(username, ctx.RequestAborted);
    if (result.Status == LabProfileStatus.Unavailable || result.Profile is null &&
        result.Status != LabProfileStatus.NotFound)
        return Results.Json(new { error = "LAB character service unavailable." },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    if (result.Status == LabProfileStatus.NotFound || result.Profile is null ||
        !result.Profile.Characters.Any(c => string.Equals(c.Name, requested,
            StringComparison.OrdinalIgnoreCase)))
        return Results.NotFound();

    var lease = leases.Reserve(username, requested, sessionKey);
    return lease switch
    {
        LabCharacterLeases.AcquireResult.Reserved => Results.Ok(new
        {
            name = requested,
            expiresInSeconds = 90,
            worldAccessGranted = false
        }),
        LabCharacterLeases.AcquireResult.Conflict =>
            Results.Conflict(new { error = "Character already reserved by another session." }),
        _ => Results.Json(new { error = "LAB character lease capacity exceeded." },
            statusCode: StatusCodes.Status503ServiceUnavailable)
    };
});

app.MapPost("/v1/characters/renew", (
    CharacterReservationRequest input, HttpContext ctx, LabSessions sessions,
    LabCharacterLeases leases) =>
{
    if (!sessions.TryRead(ctx.Request.Headers.Authorization.ToString(),
            out var username, out var sessionKey) || username is null || sessionKey is null)
        return Results.Unauthorized();
    var requested = input.Name?.Trim() ?? "";
    if (requested.Length is < 1 or > 64) return Results.BadRequest();
    return leases.Renew(username, requested, sessionKey)
        ? Results.Ok(new { name = requested, expiresInSeconds = 90, worldAccessGranted = false })
        : Results.Conflict(new { error = "Character lease not held by this session." });
});

app.MapPost("/v1/characters/release", (
    CharacterReservationRequest input, HttpContext ctx, LabSessions sessions,
    LabCharacterLeases leases) =>
{
    if (!sessions.TryRead(ctx.Request.Headers.Authorization.ToString(),
            out var username, out var sessionKey) || username is null || sessionKey is null)
        return Results.Unauthorized();
    var requested = input.Name?.Trim() ?? "";
    if (requested.Length is < 1 or > 64) return Results.BadRequest();
    return leases.Release(username, requested, sessionKey)
        ? Results.NoContent()
        : Results.Conflict(new { error = "Character lease not held by this session." });
});

app.MapPost("/v1/logout", (
    HttpContext ctx, LabSessions sessions, LabCharacterLeases leases) =>
{
    var header = ctx.Request.Headers.Authorization.ToString();
    if (!sessions.TryRead(header, out _, out var key) || key is null ||
        !sessions.Revoke(header))
        return Results.Unauthorized();
    leases.ReleaseForSession(key);
    return Results.NoContent();
});

app.Run();

internal sealed record LoginInput(string? Username, string? Password);
internal sealed record CharacterReservationRequest(string? Name);
