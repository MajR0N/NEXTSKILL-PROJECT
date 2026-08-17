using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Net.Http.Headers;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;
using Npgsql;

namespace NextSkill.Api.Pages.Org;

/// <summary>
/// The organiser-facing boundary for game-server credentials (decisions 007,
/// 012, 036). The page may project health across explicitly authorised tenants,
/// but never projects <see cref="GameServer.HmacSecret"/>. A fresh credential
/// exists in memory only for the registration response that created it.
/// </summary>
public class ServersModel(
    NextSkillDb db,
    TenantContext tenant,
    SecretProtector secrets,
    IStringLocalizer<UiText> text) : PageModel
{
    public sealed class RegistrationInput
    {
        public Guid OrganiserId { get; set; }
        public string Host { get; set; } = "";
        public int Port { get; set; } = 27015;
        public Game Game { get; set; } = Game.Cs2;
    }

    public sealed record OrganiserChoice(Guid Id, string Name);

    public sealed record ServerRow(
        Guid Id,
        string Organiser,
        string Host,
        int Port,
        Game Game,
        bool Verified,
        string? PluginVersion,
        DateTimeOffset? LastHeartbeatAt);

    [BindProperty]
    public RegistrationInput Input { get; set; } = new();

    public IReadOnlyList<OrganiserChoice> RegisterableOrganisers { get; private set; } = [];
    public IReadOnlyList<ServerRow> Servers { get; private set; } = [];
    public string? OneTimeSecret { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        PreventSecretCaching();
        if (await LoadAsync(cancellationToken) is { } denied) return denied;

        if (RegisterableOrganisers.Count > 0)
            Input.OrganiserId = RegisterableOrganisers[0].Id;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        PreventSecretCaching();

        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();
        if (!AccessPolicy.Can(actor, Operation.RegisterServer, new ResourceRef(Input.OrganiserId)))
        {
            // A refusal is the case per-row attribution can never cover: nothing
            // is written, so there is no row to attach the attempt to. Saved on
            // its own precisely because the decision it describes did not happen
            // (036:173, 051 §7).
            Audit.Record(
                db,
                actor.AccountId,
                AuditAction.RegisterServer,
                DateTimeOffset.UtcNow,
                AuditOutcome.Denied,
                organiserId: Input.OrganiserId,
                retention: AuditRetention.Security);
            await db.SaveChangesAsync(cancellationToken);
            return Forbid();
        }
        if (!await db.Organisers.AnyAsync(o => o.Id == Input.OrganiserId, cancellationToken))
            return NotFound();

        ValidateInput();
        if (!ModelState.IsValid)
        {
            if (await LoadAsync(cancellationToken) is { } denied) return denied;
            return Page();
        }

        var secret = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var server = new GameServer
        {
            Id = Guid.NewGuid(),
            OrganiserId = Input.OrganiserId,
            Host = Input.Host.Trim().ToLowerInvariant(),
            Port = Input.Port,
            Game = Input.Game,
            Verified = false,
            // Encrypted before it is ever written. The plaintext lives only long
            // enough to render one no-store response, exactly as it did before
            // — what changes is that the database no longer holds it (012).
            HmacSecret = secrets.Protect(secret),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        tenant.Set(Input.OrganiserId);
        db.Servers.Add(server);

        // Named in 036's list of actions requiring a recorded human decision.
        // The secret minted three lines above is never part of it (012 §7).
        Audit.Record(
            db,
            actor.AccountId,
            AuditAction.RegisterServer,
            server.CreatedAt,
            organiserId: Input.OrganiserId,
            resourceId: server.Id,
            reason: $"{server.Host}:{server.Port}");

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException pg
            && pg.SqlState == PostgresErrorCodes.UniqueViolation
            && string.Equals(
                pg.ConstraintName,
                "IX_servers_organiser_id_host_port",
                StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(string.Empty, text["Servers.Error.Duplicate"]);
            if (await LoadAsync(cancellationToken) is { } denied) return denied;
            return Page();
        }

        OneTimeSecret = secret;
        if (await LoadAsync(cancellationToken) is { } loadDenied) return loadDenied;
        return Page();
    }

    private async Task<IActionResult?> LoadAsync(CancellationToken cancellationToken)
    {
        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();

        var organiserRoles = actor.Organisers?.Keys.ToHashSet() ?? [];
        var allOrganisers = await db.Organisers.AsNoTracking()
            .Where(o => organiserRoles.Contains(o.Id) || actor.Platform != null)
            .OrderBy(o => o.Name)
            .Select(o => new OrganiserChoice(o.Id, o.Name))
            .ToListAsync(cancellationToken);

        var visible = allOrganisers
            .Where(o => AccessPolicy.Can(actor, Operation.ViewServerHealth, new ResourceRef(o.Id)))
            .ToList();

        RegisterableOrganisers = allOrganisers
            .Where(o => AccessPolicy.Can(actor, Operation.RegisterServer, new ResourceRef(o.Id)))
            .ToList();

        if (visible.Count == 0 && RegisterableOrganisers.Count == 0) return Forbid();

        var visibleIds = visible.Select(o => o.Id).ToArray();
        Servers = await db.Servers
            // This is an intentional cross-tenant management read. Authority is
            // reduced to concrete organiser ids before the filter is bypassed,
            // and the projection deliberately has no credential field (036).
            .IgnoreQueryFilters()
            .Where(s => visibleIds.Contains(s.OrganiserId))
            .Join(db.Organisers,
                server => server.OrganiserId,
                organiser => organiser.Id,
                (server, organiser) => new { Server = server, Organiser = organiser.Name })
            .AsNoTracking()
            .OrderBy(x => x.Organiser)
            .ThenBy(x => x.Server.Host)
            .ThenBy(x => x.Server.Port)
            .Select(x => new ServerRow(
                x.Server.Id,
                x.Organiser,
                x.Server.Host,
                x.Server.Port,
                x.Server.Game,
                x.Server.Verified,
                x.Server.PluginVersion,
                x.Server.LastHeartbeatAt))
            .ToListAsync(cancellationToken);

        return null;
    }

    private void ValidateInput()
    {
        Input.Host = Input.Host.Trim();
        if (Input.Host.Length is < 1 or > 253 || Uri.CheckHostName(Input.Host) == UriHostNameType.Unknown)
            ModelState.AddModelError("Input.Host", text["Servers.Error.Host"]);

        if (Input.Port is < 1 or > IPEndPoint.MaxPort)
            ModelState.AddModelError("Input.Port", text["Servers.Error.Port"]);

        if (!Enum.IsDefined(Input.Game))
            ModelState.AddModelError("Input.Game", text["Servers.Error.Game"]);
    }

    private void PreventSecretCaching()
    {
        Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true,
        };
        Response.Headers.Pragma = "no-cache";
    }
}
