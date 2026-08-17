using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Contracts;

namespace NextSkill.Api.Shared.Auth;

/// <summary>
/// Authenticates plugin requests by HMAC and establishes the tenant from the
/// verified server (decisions 009, 012, 037).
///
/// Game servers are third-party infrastructure (decision 007), so this is a
/// trust boundary: nothing is believed until the signature verifies.
/// </summary>
public sealed class PluginAuthFilter : IEndpointFilter
{
    /// <summary>Key under which the verified raw body is stashed for handlers.</summary>
    public const string RawBodyItem = "nextskill.raw_body";
    public const string ServerItem = "nextskill.server";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var db = http.RequestServices.GetRequiredService<NextSkillDb>();
        var tenant = http.RequestServices.GetRequiredService<TenantContext>();
        var clock = http.RequestServices.GetRequiredService<TimeProvider>();

        if (!Guid.TryParse((string?)http.Request.RouteValues["serverId"], out var serverId))
            return Problem(http, 400, "malformed-server-id", "The server id in the route is not a valid identifier.");

        // Contract version first: an unknown version must be rejected loudly
        // rather than best-effort parsed (decision 035).
        var version = http.Request.Headers[WireContract.VersionHeader].FirstOrDefault();
        if (!int.TryParse(version, out var contract) || contract != WireContract.Version)
        {
            return Problem(http, 400, "unsupported-contract",
                $"Contract version '{version}' is not supported. This server expects {WireContract.Version}. Update the plugin.");
        }

        // Read the RAW bytes. The signature covers exactly what was sent —
        // verifying against a re-serialised object would fail honest requests
        // and could pass altered ones (decision 012).
        http.Request.EnableBuffering();
        using var buffer = new MemoryStream();
        await http.Request.Body.CopyToAsync(buffer);
        var rawBody = buffer.ToArray();
        http.Request.Body.Position = 0;

        // Deliberate, reviewed use of IgnoreQueryFilters (decision 036): the
        // tenant is not known yet — the whole point of this lookup is to
        // discover which organiser owns the signing server. Every other query in
        // the request runs scoped normally.
        var server = await db.Servers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == serverId);

        // Unknown server and bad signature return the SAME response. Otherwise
        // an attacker could enumerate which server ids exist.
        // Decrypted here and nowhere else. A key the database does not hold is
        // the whole point: a stolen dump no longer contains a signing
        // credential (012). An unrecoverable value fails closed as a 401,
        // exactly like a wrong signature.
        var secret = http.RequestServices.GetRequiredService<SecretProtector>()
            .Reveal(server?.HmacSecret);

        // Unknown server, unreadable credential and bad signature all return the
        // SAME response. Otherwise an attacker could enumerate which server ids
        // exist.
        if (secret is null)
            return Problem(http, 401, "unauthorised", "Signature verification failed.");

        var result = RequestSignature.Verify(
            rawBody,
            http.Request.Headers[WireContract.SignatureHeader].FirstOrDefault(),
            http.Request.Headers[WireContract.TimestampHeader].FirstOrDefault(),
            secret,
            clock.GetUtcNow());

        if (result != SignatureResult.Valid)
        {
            return Problem(http, 401, "unauthorised", result switch
            {
                SignatureResult.Expired => "Request timestamp is outside the accepted window. Check the server clock.",
                SignatureResult.Malformed => "Signature or timestamp header missing or malformed.",
                _ => "Signature verification failed.",
            });
        }

        // Verified: the request genuinely came from this server, so its
        // organiser becomes the tenant for the rest of the pipeline.
        tenant.Set(server.OrganiserId);

        http.Items[RawBodyItem] = rawBody;
        http.Items[ServerItem] = server;

        return await next(ctx);
    }

    /// <summary>RFC 7807 problem response (decision 009).</summary>
    private static IResult Problem(HttpContext http, int status, string type, string detail) =>
        Results.Problem(
            title: type.Replace('-', ' '),
            detail: detail,
            statusCode: status,
            type: $"https://nextskill.example/errors/{type}",
            instance: http.Request.Path);
}
