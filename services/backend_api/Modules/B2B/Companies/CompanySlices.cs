using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.B2B.Authorization;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.B2B.Quotes;
using BackendApi.Modules.Shared;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BackendApi.Modules.B2B.Companies;

// =============================================================================
// Spec 021 Phase 7 (US4) — company-account customer-side admin slices.
// All slices share a compact pattern: Request record + Handler class + endpoint
// extension. Authority gates: customer-owner OR companies.admin membership;
// per-slice deviations are noted inline.
// =============================================================================

#region 5.1 RegisterCompany — POST /api/customer/companies

public sealed record RegisterCompanyRequest(
    [property: JsonPropertyName("name")] LocalizedName Name,
    [property: JsonPropertyName("tax_id")] string TaxId,
    [property: JsonPropertyName("market_code")] string MarketCode,
    [property: JsonPropertyName("primary_address")] JsonElement? PrimaryAddress,
    [property: JsonPropertyName("billing_address")] JsonElement? BillingAddress,
    [property: JsonPropertyName("approver_required")] bool? ApproverRequired,
    [property: JsonPropertyName("po_required")] bool? PoRequired,
    [property: JsonPropertyName("unique_po_required")] bool? UniquePoRequired);

public sealed record LocalizedName(
    [property: JsonPropertyName("en")] string? En,
    [property: JsonPropertyName("ar")] string? Ar);

public sealed record CompanySummary(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("market_code")] string MarketCode,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("approver_required")] bool ApproverRequired,
    [property: JsonPropertyName("po_required")] bool PoRequired,
    [property: JsonPropertyName("unique_po_required")] bool UniquePoRequired,
    [property: JsonPropertyName("invoice_billing_eligible")] bool InvoiceBillingEligible);

public sealed class RegisterCompanyValidator : AbstractValidator<RegisterCompanyRequest>
{
    public RegisterCompanyValidator()
    {
        RuleFor(x => x.Name).NotNull()
            .Must(n => !string.IsNullOrWhiteSpace(n.En) || !string.IsNullOrWhiteSpace(n.Ar))
            .WithErrorCode(QuoteReasonCode.QuoteRequiredFieldMissing.ToToken());
        RuleFor(x => x.TaxId).NotEmpty()
            .WithErrorCode(QuoteReasonCode.CompanyTaxIdInvalid.ToToken());
        RuleFor(x => x.MarketCode).NotEmpty()
            .WithErrorCode(QuoteReasonCode.QuoteMarketMismatch.ToToken());
    }
}

public sealed class RegisterCompanyHandler
{
    private readonly B2BDbContext _db;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _time;
    public RegisterCompanyHandler(B2BDbContext db, IAuditEventPublisher audit, TimeProvider time)
    { _db = db; _audit = audit; _time = time; }

    public async Task<CompanyResult> HandleAsync(Guid actorId, string callerMarket, RegisterCompanyRequest req, CancellationToken ct)
    {
        var market = (req.MarketCode ?? callerMarket).ToLowerInvariant();
        if (!string.Equals(market, callerMarket, StringComparison.OrdinalIgnoreCase))
        {
            return CompanyResult.Reject(422, QuoteReasonCode.QuoteMarketMismatch);
        }

        // Per-market schema for company_verification_required toggle.
        var schema = await _db.QuoteMarketSchemas.AsNoTracking()
            .Where(s => s.MarketCode == market && s.EffectiveTo == null)
            .OrderByDescending(s => s.Version)
            .FirstOrDefaultAsync(ct);

        // Duplicate (market, tax_id) check.
        var dupe = await _db.Companies.AsNoTracking()
            .AnyAsync(c => c.MarketCode == market && c.TaxId == req.TaxId, ct);
        if (dupe) return CompanyResult.Reject(409, QuoteReasonCode.CompanyDuplicateTaxId);

        var companyId = Guid.NewGuid();
        var nowUtc = _time.GetUtcNow();
        var initialState = (schema?.CompanyVerificationRequired ?? false) ? "pending-verification" : "active";

        var company = new Company
        {
            Id = companyId,
            NameJson = JsonSerializer.Serialize(new { en = req.Name?.En ?? "", ar = req.Name?.Ar ?? "" }),
            TaxId = req.TaxId,
            MarketCode = market,
            PrimaryAddressJson = req.PrimaryAddress?.GetRawText() ?? "{}",
            BillingAddressJson = req.BillingAddress?.GetRawText(),
            ApproverRequired = req.ApproverRequired ?? true,
            PoRequired = req.PoRequired ?? false,
            UniquePoRequired = req.UniquePoRequired ?? false,
            InvoiceBillingEligible = true,
            State = initialState,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
        _db.Companies.Add(company);

        // Caller becomes both companies.admin and buyer.
        _db.CompanyMemberships.Add(new CompanyMembership
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            MarketCode = market,
            UserId = actorId,
            Role = "companies.admin",
            JoinedAt = nowUtc,
        });
        _db.CompanyMemberships.Add(new CompanyMembership
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            MarketCode = market,
            UserId = actorId,
            Role = "buyer",
            JoinedAt = nowUtc,
        });

        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return CompanyResult.Reject(409, QuoteReasonCode.CompanyDuplicateTaxId);
        }

        try
        {
            await _audit.PublishAsync(new AuditEvent(
                ActorId: actorId, ActorRole: "customer",
                Action: "company.registered", EntityType: "company", EntityId: companyId,
                BeforeState: null,
                AfterState: new { state = initialState, market_code = market, approver_required = company.ApproverRequired },
                Reason: null), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { }

        return CompanyResult.Success(new CompanySummary(
            companyId, market, initialState,
            company.ApproverRequired, company.PoRequired,
            company.UniquePoRequired, company.InvoiceBillingEligible));
    }

    internal static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation;
}

#endregion

#region 5.2 GetMyCompany — GET /api/customer/companies/{id}

public sealed record GetMyCompanyResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("market_code")] string MarketCode,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("name")] LocalizedName Name,
    [property: JsonPropertyName("approver_required")] bool ApproverRequired,
    [property: JsonPropertyName("po_required")] bool PoRequired,
    [property: JsonPropertyName("unique_po_required")] bool UniquePoRequired,
    [property: JsonPropertyName("invoice_billing_eligible")] bool InvoiceBillingEligible,
    [property: JsonPropertyName("branches")] IReadOnlyList<BranchEntry> Branches,
    [property: JsonPropertyName("memberships")] IReadOnlyList<MembershipEntry> Memberships);

public sealed record BranchEntry(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string NameJson,
    [property: JsonPropertyName("address")] string AddressJson,
    [property: JsonPropertyName("contact_phone")] string? ContactPhone);

public sealed record MembershipEntry(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("joined_at")] DateTimeOffset JoinedAt);

public sealed class GetMyCompanyHandler
{
    private readonly B2BDbContext _db;
    public GetMyCompanyHandler(B2BDbContext db) => _db = db;

    public async Task<GetMyCompanyResponse?> HandleAsync(Guid actorId, Guid companyId, CancellationToken ct)
    {
        var hasMembership = await _db.CompanyMemberships.AsNoTracking()
            .AnyAsync(m => m.CompanyId == companyId && m.UserId == actorId, ct);
        if (!hasMembership) return null;

        var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, ct);
        if (company is null) return null;

        var branches = await _db.CompanyBranches.AsNoTracking()
            .Where(b => b.CompanyId == companyId)
            .Select(b => new BranchEntry(b.Id, b.NameJson, b.AddressJson, b.ContactPhone))
            .ToListAsync(ct);

        var memberships = await _db.CompanyMemberships.AsNoTracking()
            .Where(m => m.CompanyId == companyId)
            .Select(m => new MembershipEntry(m.Id, m.UserId, m.Role, m.JoinedAt))
            .ToListAsync(ct);

        var name = TryParseName(company.NameJson);
        return new GetMyCompanyResponse(
            company.Id, company.MarketCode, company.State, name,
            company.ApproverRequired, company.PoRequired, company.UniquePoRequired,
            company.InvoiceBillingEligible, branches, memberships);
    }

    private static LocalizedName TryParseName(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            string? en = null, ar = null;
            if (doc.RootElement.TryGetProperty("en", out var e)) en = e.GetString();
            if (doc.RootElement.TryGetProperty("ar", out var a)) ar = a.GetString();
            return new LocalizedName(en, ar);
        }
        catch (JsonException) { return new LocalizedName(null, null); }
    }
}

#endregion

#region 5.3 UpdateCompanyConfig — PATCH /api/customer/companies/{id}

public sealed record UpdateCompanyConfigRequest(
    [property: JsonPropertyName("approver_required")] bool? ApproverRequired,
    [property: JsonPropertyName("po_required")] bool? PoRequired,
    [property: JsonPropertyName("unique_po_required")] bool? UniquePoRequired);

public sealed class UpdateCompanyConfigHandler
{
    private readonly B2BDbContext _db;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _time;

    public UpdateCompanyConfigHandler(B2BDbContext db, IAuditEventPublisher audit, TimeProvider time)
    { _db = db; _audit = audit; _time = time; }

    public async Task<CompanyResult> HandleAsync(Guid actorId, Guid companyId, UpdateCompanyConfigRequest req, CancellationToken ct)
    {
        var hasAdmin = await _db.CompanyMemberships.AsNoTracking()
            .AnyAsync(m => m.CompanyId == companyId && m.UserId == actorId && m.Role == "companies.admin", ct);
        if (!hasAdmin) return CompanyResult.Reject(404, QuoteReasonCode.QuoteNotFound);

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct);
        if (company is null) return CompanyResult.Reject(404, QuoteReasonCode.QuoteNotFound);

        var prevApprover = company.ApproverRequired;
        if (req.ApproverRequired.HasValue) company.ApproverRequired = req.ApproverRequired.Value;
        if (req.PoRequired.HasValue) company.PoRequired = req.PoRequired.Value;
        if (req.UniquePoRequired.HasValue) company.UniquePoRequired = req.UniquePoRequired.Value;
        company.UpdatedAt = _time.GetUtcNow();

        // FR-031: when toggling approver_required from true → false while quotes are
        // pending-approver, transition them back to revised so they don't auto-finalize.
        if (prevApprover && !company.ApproverRequired)
        {
            var pending = await _db.Quotes
                .Where(q => q.CompanyId == companyId && q.State == "pending-approver")
                .ToListAsync(ct);
            foreach (var q in pending)
            {
                var prior = q.State;
                q.State = QuoteState.Revised.ToToken();
                _db.QuoteStateTransitions.Add(new QuoteStateTransition
                {
                    Id = Guid.NewGuid(),
                    QuoteId = q.Id,
                    MarketCode = q.MarketCode,
                    PriorState = prior,
                    NewState = q.State,
                    ActorKind = QuoteActorKind.System.ToToken(),
                    ActorId = actorId,
                    ReasonJson = null,
                    MetadataJson = "{\"reason\":\"approver_required_disabled\"}",
                    OccurredAt = company.UpdatedAt,
                });
            }
        }

        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            return CompanyResult.Reject(409, QuoteReasonCode.QuoteInvalidStateForAction);
        }

        try
        {
            await _audit.PublishAsync(new AuditEvent(
                ActorId: actorId, ActorRole: "companies.admin",
                Action: "company.config_updated", EntityType: "company", EntityId: companyId,
                BeforeState: new { approver_required = prevApprover },
                AfterState: new { approver_required = company.ApproverRequired, po_required = company.PoRequired, unique_po_required = company.UniquePoRequired },
                Reason: null), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { }

        return CompanyResult.Success(new CompanySummary(
            company.Id, company.MarketCode, company.State,
            company.ApproverRequired, company.PoRequired,
            company.UniquePoRequired, company.InvoiceBillingEligible));
    }
}

#endregion

#region 5.4 / 5.5 Branch CRUD

public sealed record AddBranchRequest(
    [property: JsonPropertyName("name")] LocalizedName Name,
    [property: JsonPropertyName("address")] JsonElement? Address,
    [property: JsonPropertyName("contact_phone")] string? ContactPhone);

public sealed class BranchHandler
{
    private readonly B2BDbContext _db;
    private readonly IAuditEventPublisher _audit;
    public BranchHandler(B2BDbContext db, IAuditEventPublisher audit)
    { _db = db; _audit = audit; }

    public async Task<CompanyResult> AddAsync(Guid actorId, Guid companyId, AddBranchRequest req, CancellationToken ct)
    {
        var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, ct);
        if (company is null) return CompanyResult.Reject(404, QuoteReasonCode.QuoteNotFound);
        var hasAdmin = await _db.CompanyMemberships.AsNoTracking()
            .AnyAsync(m => m.CompanyId == companyId && m.UserId == actorId && m.Role == "companies.admin", ct);
        if (!hasAdmin) return CompanyResult.Reject(404, QuoteReasonCode.QuoteNotFound);

        var branchId = Guid.NewGuid();
        _db.CompanyBranches.Add(new CompanyBranch
        {
            Id = branchId,
            CompanyId = companyId,
            MarketCode = company.MarketCode,
            NameJson = JsonSerializer.Serialize(new { en = req.Name?.En ?? "", ar = req.Name?.Ar ?? "" }),
            AddressJson = req.Address?.GetRawText() ?? "{}",
            ContactPhone = req.ContactPhone,
        });
        await _db.SaveChangesAsync(ct);

        // CodeRabbit Round 1 — Principle 25: structural changes to company data are
        // audited.
        try
        {
            await _audit.PublishAsync(new AuditEvent(
                ActorId: actorId, ActorRole: "companies.admin",
                Action: "company.branch_added", EntityType: "company_branch", EntityId: branchId,
                BeforeState: null,
                AfterState: new { company_id = companyId, market_code = company.MarketCode },
                Reason: null), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { }

        return CompanyResult.SuccessWithId(branchId);
    }

    public async Task<CompanyResult> RemoveAsync(Guid actorId, Guid companyId, Guid branchId, CancellationToken ct)
    {
        var hasAdmin = await _db.CompanyMemberships.AsNoTracking()
            .AnyAsync(m => m.CompanyId == companyId && m.UserId == actorId && m.Role == "companies.admin", ct);
        if (!hasAdmin) return CompanyResult.Reject(404, QuoteReasonCode.QuoteNotFound);

        var branch = await _db.CompanyBranches.FirstOrDefaultAsync(b => b.Id == branchId && b.CompanyId == companyId, ct);
        if (branch is null) return CompanyResult.Reject(404, QuoteReasonCode.QuoteNotFound);

        var referenced = await _db.Quotes.AsNoTracking()
            .AnyAsync(q => q.BranchId == branchId
                && q.State != "accepted" && q.State != "rejected"
                && q.State != "expired" && q.State != "withdrawn", ct);
        if (referenced) return CompanyResult.Reject(409, QuoteReasonCode.QuoteInvalidStateForAction);

        _db.CompanyBranches.Remove(branch);
        await _db.SaveChangesAsync(ct);

        try
        {
            await _audit.PublishAsync(new AuditEvent(
                ActorId: actorId, ActorRole: "companies.admin",
                Action: "company.branch_removed", EntityType: "company_branch", EntityId: branchId,
                BeforeState: new { company_id = companyId },
                AfterState: null,
                Reason: null), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { }

        return CompanyResult.Success(null);
    }
}

#endregion

#region 5.6 / 5.7 / 5.8 Invitation lifecycle

public sealed record InviteUserRequest(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("role")] string Role);

public sealed record InvitationTokenResponse(
    [property: JsonPropertyName("invitation_id")] Guid InvitationId,
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);

public sealed class InvitationHandler
{
    private readonly B2BDbContext _db;
    private readonly CompanyInvitationTokenHasher _hasher;
    private readonly IAuditEventPublisher _audit;
    private readonly IPublisher _domain;
    private readonly TimeProvider _time;

    public InvitationHandler(
        B2BDbContext db, CompanyInvitationTokenHasher hasher,
        IAuditEventPublisher audit, IPublisher domain, TimeProvider time)
    {
        _db = db; _hasher = hasher; _audit = audit; _domain = domain; _time = time;
    }

    public async Task<CompanyResult> InviteAsync(Guid actorId, Guid companyId, InviteUserRequest req, CancellationToken ct)
    {
        var hasAdmin = await _db.CompanyMemberships.AsNoTracking()
            .AnyAsync(m => m.CompanyId == companyId && m.UserId == actorId && m.Role == "companies.admin", ct);
        if (!hasAdmin) return CompanyResult.Reject(404, QuoteReasonCode.QuoteNotFound);

        if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@'))
        {
            return CompanyResult.Reject(400, QuoteReasonCode.CompanyInvitationEmailInvalid);
        }
        if (req.Role != "buyer" && req.Role != "approver" && req.Role != "companies.admin")
        {
            return CompanyResult.Reject(400, QuoteReasonCode.QuoteRequiredFieldMissing);
        }

        var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, ct);
        if (company is null) return CompanyResult.Reject(404, QuoteReasonCode.QuoteNotFound);

        var schema = await _db.QuoteMarketSchemas.AsNoTracking()
            .Where(s => s.MarketCode == company.MarketCode && s.EffectiveTo == null)
            .OrderByDescending(s => s.Version)
            .FirstOrDefaultAsync(ct);
        var ttlDays = schema?.InvitationTtlDays ?? 14;

        var emailNorm = req.Email.Trim().ToLowerInvariant();
        var existingPending = await _db.CompanyInvitations.AsNoTracking()
            .AnyAsync(i => i.CompanyId == companyId && i.InvitedEmail == emailNorm
                && i.TargetRole == req.Role && i.State == "pending", ct);
        if (existingPending) return CompanyResult.Reject(409, QuoteReasonCode.CompanyInvitationAlreadyPending);

        var plaintext = GenerateUrlSafeToken();
        var hash = _hasher.Hash(plaintext);
        var nowUtc = _time.GetUtcNow();
        var inv = new CompanyInvitation
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            MarketCode = company.MarketCode,
            InvitedBy = actorId,
            InvitedEmail = emailNorm,
            TargetRole = req.Role,
            TokenHash = hash,
            State = "pending",
            SentAt = nowUtc,
            ExpiresAt = nowUtc.AddDays(ttlDays),
        };
        _db.CompanyInvitations.Add(inv);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (RegisterCompanyHandler.IsUniqueViolation(ex))
        {
            return CompanyResult.Reject(409, QuoteReasonCode.CompanyInvitationAlreadyPending);
        }

        try
        {
            await _domain.Publish(new CompanyInvitationSent(
                inv.Id, companyId, emailNorm, req.Role,
                LocaleHint: company.MarketCode == "ksa" ? "ar" : "en",
                ActorId: actorId,
                PerformedAt: nowUtc), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { }

        return CompanyResult.SuccessToken(new InvitationTokenResponse(inv.Id, plaintext, inv.ExpiresAt));
    }

    public async Task<CompanyResult> AcceptAsync(Guid actorId, string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return CompanyResult.Reject(400, QuoteReasonCode.QuoteRequiredFieldMissing);
        }
        var hash = _hasher.Hash(token);
        var inv = await _db.CompanyInvitations
            .FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (inv is null) return CompanyResult.Reject(404, QuoteReasonCode.QuoteNotFound);
        var nowUtc = _time.GetUtcNow();
        if (inv.State != "pending" || inv.ExpiresAt <= nowUtc)
        {
            return CompanyResult.Reject(409, QuoteReasonCode.CompanyInvitationExpired);
        }

        var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == inv.CompanyId, ct);
        if (company is null) return CompanyResult.Reject(404, QuoteReasonCode.QuoteNotFound);

        // De-duplicate: if the user already has the target role, skip the insert
        // but still mark the invitation accepted (CompanyMemberAlreadyExists is a
        // hint, not a hard reject).
        var dupe = await _db.CompanyMemberships.AsNoTracking()
            .AnyAsync(m => m.CompanyId == inv.CompanyId && m.UserId == actorId && m.Role == inv.TargetRole, ct);
        if (!dupe)
        {
            _db.CompanyMemberships.Add(new CompanyMembership
            {
                Id = Guid.NewGuid(),
                CompanyId = inv.CompanyId,
                MarketCode = company.MarketCode,
                UserId = actorId,
                Role = inv.TargetRole,
                JoinedAt = nowUtc,
            });
        }
        inv.State = "accepted";
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (RegisterCompanyHandler.IsUniqueViolation(ex))
        {
            // Concurrent accept on same membership — treat as benign.
        }

        try
        {
            await _domain.Publish(new CompanyInvitationAccepted(
                inv.Id, inv.CompanyId, actorId, inv.TargetRole,
                ActorId: actorId, PerformedAt: nowUtc), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { }

        return CompanyResult.Success(null);
    }

    public async Task<CompanyResult> DeclineAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return CompanyResult.Reject(400, QuoteReasonCode.QuoteRequiredFieldMissing);
        }
        var hash = _hasher.Hash(token);
        var inv = await _db.CompanyInvitations.FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (inv is null) return CompanyResult.Reject(404, QuoteReasonCode.QuoteNotFound);
        if (inv.State != "pending") return CompanyResult.Reject(409, QuoteReasonCode.CompanyInvitationExpired);
        inv.State = "declined";
        await _db.SaveChangesAsync(ct);
        try
        {
            await _domain.Publish(new CompanyInvitationDeclined(
                inv.Id, inv.CompanyId, inv.InvitedEmail,
                ActorId: Guid.Empty, PerformedAt: _time.GetUtcNow()), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { }
        return CompanyResult.Success(null);
    }

    private static string GenerateUrlSafeToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}

#endregion

#region 5.9 / 5.10 Member CRUD with FR-024 / FR-025 / FR-030 invariants

public sealed record ChangeMemberRoleRequest(
    [property: JsonPropertyName("role")] string Role);

public sealed class MemberHandler
{
    private readonly B2BDbContext _db;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _time;

    public MemberHandler(B2BDbContext db, IAuditEventPublisher audit, TimeProvider time)
    { _db = db; _audit = audit; _time = time; }

    public async Task<CompanyResult> RemoveAsync(Guid actorId, Guid companyId, Guid membershipId, CancellationToken ct)
    {
        var hasAdmin = await _db.CompanyMemberships.AsNoTracking()
            .AnyAsync(m => m.CompanyId == companyId && m.UserId == actorId && m.Role == "companies.admin", ct);
        if (!hasAdmin) return CompanyResult.Reject(404, QuoteReasonCode.QuoteNotFound);

        var target = await _db.CompanyMemberships
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.CompanyId == companyId, ct);
        if (target is null) return CompanyResult.Reject(404, QuoteReasonCode.QuoteNotFound);

        return await ApplyMembershipChange(actorId, companyId, target, removalOnly: true, newRole: null, ct);
    }

    public async Task<CompanyResult> ChangeRoleAsync(Guid actorId, Guid companyId, Guid membershipId, ChangeMemberRoleRequest req, CancellationToken ct)
    {
        if (req.Role != "buyer" && req.Role != "approver" && req.Role != "companies.admin")
        {
            return CompanyResult.Reject(400, QuoteReasonCode.QuoteRequiredFieldMissing);
        }
        var hasAdmin = await _db.CompanyMemberships.AsNoTracking()
            .AnyAsync(m => m.CompanyId == companyId && m.UserId == actorId && m.Role == "companies.admin", ct);
        if (!hasAdmin) return CompanyResult.Reject(404, QuoteReasonCode.QuoteNotFound);

        var target = await _db.CompanyMemberships
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.CompanyId == companyId, ct);
        if (target is null) return CompanyResult.Reject(404, QuoteReasonCode.QuoteNotFound);

        return await ApplyMembershipChange(actorId, companyId, target, removalOnly: false, newRole: req.Role, ct);
    }

    private async Task<CompanyResult> ApplyMembershipChange(
        Guid actorId, Guid companyId, CompanyMembership target,
        bool removalOnly, string? newRole, CancellationToken ct)
    {
        // CodeRabbit Round 1: capture the original role BEFORE any mutation so the
        // audit BeforeState reports the pre-change role, not the new one.
        var originalRole = target.Role;

        var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, ct);
        if (company is null) return CompanyResult.Reject(404, QuoteReasonCode.QuoteNotFound);

        var allMemberships = await _db.CompanyMemberships
            .Where(m => m.CompanyId == companyId).ToListAsync(ct);

        // FR-024 — last admin can never be removed.
        if (target.Role == "companies.admin"
            && (removalOnly || newRole != "companies.admin"))
        {
            var adminCount = allMemberships.Count(m => m.Role == "companies.admin");
            if (adminCount <= 1)
            {
                return CompanyResult.Reject(409, QuoteReasonCode.CompanyLastAdminCannotBeRemoved);
            }
        }

        // FR-025 — when company.approver_required=true, removing the last approver
        // is forbidden.
        var willRemoveApprover = removalOnly && target.Role == "approver";
        var willChangeFromApprover = !removalOnly && target.Role == "approver" && newRole != "approver";
        if ((willRemoveApprover || willChangeFromApprover) && company.ApproverRequired)
        {
            var approverCount = allMemberships.Count(m => m.Role == "approver");
            if (approverCount <= 1)
            {
                return CompanyResult.Reject(409, QuoteReasonCode.CompanyLastApproverCannotBeRemovedWithRequired);
            }
        }

        if (removalOnly)
        {
            _db.CompanyMemberships.Remove(target);
        }
        else
        {
            target.Role = newRole!;
        }

        // FR-030 — if removal results in zero approvers AND approver_required=true,
        // any pending-approver quotes for this company transition back to revised.
        var nowUtc = _time.GetUtcNow();
        var willHaveApprovers = allMemberships
            .Where(m => m.Id != target.Id || (!removalOnly && newRole == "approver"))
            .Any(m => (m.Id == target.Id ? newRole : m.Role) == "approver");
        if (company.ApproverRequired && !willHaveApprovers)
        {
            var pending = await _db.Quotes
                .Where(q => q.CompanyId == companyId && q.State == "pending-approver")
                .ToListAsync(ct);
            foreach (var q in pending)
            {
                var prior = q.State;
                q.State = QuoteState.Revised.ToToken();
                _db.QuoteStateTransitions.Add(new QuoteStateTransition
                {
                    Id = Guid.NewGuid(),
                    QuoteId = q.Id,
                    MarketCode = q.MarketCode,
                    PriorState = prior,
                    NewState = q.State,
                    ActorKind = QuoteActorKind.System.ToToken(),
                    ActorId = actorId,
                    ReasonJson = null,
                    MetadataJson = "{\"reason\":\"last_approver_left\"}",
                    OccurredAt = nowUtc,
                });
            }
        }

        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            return CompanyResult.Reject(409, QuoteReasonCode.QuoteInvalidStateForAction);
        }

        try
        {
            await _audit.PublishAsync(new AuditEvent(
                ActorId: actorId, ActorRole: "companies.admin",
                Action: removalOnly ? "company.member_removed" : "company.member_role_changed",
                EntityType: "company_membership", EntityId: target.Id,
                BeforeState: new { role = originalRole },
                AfterState: removalOnly ? null : new { role = newRole },
                Reason: null), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { }

        return CompanyResult.Success(null);
    }
}

#endregion

#region 6.1 SuspendCompany — admin-side

public sealed record SuspendCompanyRequest(
    [property: JsonPropertyName("reason")] string? Reason);

public sealed class SuspendCompanyHandler
{
    private readonly B2BDbContext _db;
    private readonly IAuditEventPublisher _audit;
    private readonly TimeProvider _time;

    public SuspendCompanyHandler(B2BDbContext db, IAuditEventPublisher audit, TimeProvider time)
    { _db = db; _audit = audit; _time = time; }

    public async Task<CompanyResult> HandleAsync(Guid adminId, Guid companyId, SuspendCompanyRequest req, CancellationToken ct)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct);
        if (company is null) return CompanyResult.Reject(404, QuoteReasonCode.QuoteNotFound);
        var prior = company.State;
        company.State = "suspended";
        company.UpdatedAt = _time.GetUtcNow();
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return CompanyResult.Reject(409, QuoteReasonCode.QuoteInvalidStateForAction); }

        try
        {
            await _audit.PublishAsync(new AuditEvent(
                ActorId: adminId, ActorRole: "admin",
                Action: "company.suspended", EntityType: "company", EntityId: companyId,
                BeforeState: new { state = prior },
                AfterState: new { state = "suspended" },
                Reason: req.Reason), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { }
        return CompanyResult.Success(null);
    }
}

#endregion

#region Result envelope + endpoint mapping

public sealed record CompanyResult(
    bool IsSuccess,
    int StatusCode,
    QuoteReasonCode? ReasonCode,
    object? Body)
{
    public static CompanyResult Success(object? body) => new(true, 200, null, body);
    public static CompanyResult SuccessWithId(Guid id) => new(true, 201, null, new { id });
    public static CompanyResult SuccessToken(InvitationTokenResponse t) => new(true, 201, null, t);
    public static CompanyResult Reject(int status, QuoteReasonCode code) => new(false, status, code, null);
}

public static class CompanyEndpoints
{
    public static IEndpointRouteBuilder MapCompanyEndpoints(this IEndpointRouteBuilder app)
    {
        var customer = app.MapGroup("/api/customer/companies")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        customer.MapPost("/", RegisterAsync);
        customer.MapGet("/{id:guid}", GetMyAsync);
        customer.MapPatch("/{id:guid}", UpdateConfigAsync);
        customer.MapPost("/{id:guid}/branches", AddBranchAsync);
        customer.MapDelete("/{id:guid}/branches/{branchId:guid}", RemoveBranchAsync);
        customer.MapPost("/{id:guid}/invitations", InviteAsync);
        customer.MapPost("/invitations/{token}/accept", AcceptAsync);
        customer.MapPost("/invitations/{token}/decline", DeclineAsync);
        customer.MapDelete("/{id:guid}/memberships/{membershipId:guid}", RemoveMemberAsync);
        customer.MapPatch("/{id:guid}/memberships/{membershipId:guid}", ChangeMemberRoleAsync);

        var admin = app.MapGroup("/api/admin/companies")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AdminAuthorizationDefaults.AuthenticationScheme });
        admin.MapPost("/{id:guid}/suspend", SuspendAsync);
        return app;
    }

    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterCompanyRequest? body,
        HttpContext context,
        RegisterCompanyHandler handler,
        IValidator<RegisterCompanyRequest> validator,
        CancellationToken ct)
    {
        if (body is null)
        {
            return B2BResponseFactory.Problem(context, 400,
                QuoteReasonCode.QuoteRequiredFieldMissing, "Body required.");
        }
        var v = await validator.ValidateAsync(body, ct);
        if (!v.IsValid)
        {
            var first = v.Errors[0];
            return Results.Json(new
            {
                type = $"https://errors.dental-commerce/quotes/{first.ErrorCode}",
                title = "Company validation failed.",
                status = 400,
                detail = string.Join("; ", v.Errors.Select(e => e.ErrorMessage)),
                instance = context.Request.Path.ToString(),
                reasonCode = first.ErrorCode,
            }, statusCode: 400, contentType: "application/problem+json");
        }
        var actorId = B2BResponseFactory.ResolveCustomerId(context);
        var market = B2BResponseFactory.ResolveMarketCode(context);
        if (actorId is null || market is null)
        {
            return B2BResponseFactory.Problem(context, 401,
                QuoteReasonCode.QuoteRequiredFieldMissing, "Authentication required.");
        }
        var r = await handler.HandleAsync(actorId.Value, market, body, ct);
        return Translate(context, r);
    }

    private static async Task<IResult> GetMyAsync(
        Guid id, HttpContext context, GetMyCompanyHandler handler, CancellationToken ct)
    {
        var actorId = B2BResponseFactory.ResolveCustomerId(context);
        if (actorId is null)
            return B2BResponseFactory.Problem(context, 401, QuoteReasonCode.QuoteRequiredFieldMissing, "Auth required.");
        var resp = await handler.HandleAsync(actorId.Value, id, ct);
        if (resp is null) return B2BResponseFactory.Problem(context, 404, QuoteReasonCode.QuoteNotFound, "Not found.");
        return Results.Ok(resp);
    }

    private static async Task<IResult> UpdateConfigAsync(
        Guid id, [FromBody] UpdateCompanyConfigRequest? body,
        HttpContext context, UpdateCompanyConfigHandler handler, CancellationToken ct)
    {
        var actorId = B2BResponseFactory.ResolveCustomerId(context);
        if (actorId is null)
            return B2BResponseFactory.Problem(context, 401, QuoteReasonCode.QuoteRequiredFieldMissing, "Auth required.");
        body ??= new UpdateCompanyConfigRequest(null, null, null);
        var r = await handler.HandleAsync(actorId.Value, id, body, ct);
        return Translate(context, r);
    }

    private static async Task<IResult> AddBranchAsync(
        Guid id, [FromBody] AddBranchRequest? body, HttpContext context,
        BranchHandler handler, CancellationToken ct)
    {
        var actorId = B2BResponseFactory.ResolveCustomerId(context);
        if (actorId is null || body is null)
            return B2BResponseFactory.Problem(context, 400, QuoteReasonCode.QuoteRequiredFieldMissing, "Bad request.");
        var r = await handler.AddAsync(actorId.Value, id, body, ct);
        return Translate(context, r);
    }

    private static async Task<IResult> RemoveBranchAsync(
        Guid id, Guid branchId, HttpContext context, BranchHandler handler, CancellationToken ct)
    {
        var actorId = B2BResponseFactory.ResolveCustomerId(context);
        if (actorId is null)
            return B2BResponseFactory.Problem(context, 401, QuoteReasonCode.QuoteRequiredFieldMissing, "Auth required.");
        var r = await handler.RemoveAsync(actorId.Value, id, branchId, ct);
        return Translate(context, r);
    }

    private static async Task<IResult> InviteAsync(
        Guid id, [FromBody] InviteUserRequest? body, HttpContext context,
        InvitationHandler handler, CancellationToken ct)
    {
        var actorId = B2BResponseFactory.ResolveCustomerId(context);
        if (actorId is null || body is null)
            return B2BResponseFactory.Problem(context, 400, QuoteReasonCode.QuoteRequiredFieldMissing, "Bad request.");
        var r = await handler.InviteAsync(actorId.Value, id, body, ct);
        return Translate(context, r);
    }

    private static async Task<IResult> AcceptAsync(
        string token, HttpContext context, InvitationHandler handler, CancellationToken ct)
    {
        var actorId = B2BResponseFactory.ResolveCustomerId(context);
        if (actorId is null)
            return B2BResponseFactory.Problem(context, 401, QuoteReasonCode.QuoteRequiredFieldMissing, "Auth required.");
        var r = await handler.AcceptAsync(actorId.Value, token, ct);
        return Translate(context, r);
    }

    private static async Task<IResult> DeclineAsync(
        string token, HttpContext context, InvitationHandler handler, CancellationToken ct)
    {
        var r = await handler.DeclineAsync(token, ct);
        return Translate(context, r);
    }

    private static async Task<IResult> RemoveMemberAsync(
        Guid id, Guid membershipId, HttpContext context, MemberHandler handler, CancellationToken ct)
    {
        var actorId = B2BResponseFactory.ResolveCustomerId(context);
        if (actorId is null)
            return B2BResponseFactory.Problem(context, 401, QuoteReasonCode.QuoteRequiredFieldMissing, "Auth required.");
        var r = await handler.RemoveAsync(actorId.Value, id, membershipId, ct);
        return Translate(context, r);
    }

    private static async Task<IResult> ChangeMemberRoleAsync(
        Guid id, Guid membershipId, [FromBody] ChangeMemberRoleRequest? body,
        HttpContext context, MemberHandler handler, CancellationToken ct)
    {
        var actorId = B2BResponseFactory.ResolveCustomerId(context);
        if (actorId is null || body is null)
            return B2BResponseFactory.Problem(context, 400, QuoteReasonCode.QuoteRequiredFieldMissing, "Bad request.");
        var r = await handler.ChangeRoleAsync(actorId.Value, id, membershipId, body, ct);
        return Translate(context, r);
    }

    private static async Task<IResult> SuspendAsync(
        Guid id, [FromBody] SuspendCompanyRequest? body,
        HttpContext context, SuspendCompanyHandler handler, CancellationToken ct)
    {
        if (!context.User.HasClaim("permission", B2BPermissions.CompaniesSuspend)
            && !context.User.HasClaim("permissions", B2BPermissions.CompaniesSuspend))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
        var sub = context.User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(sub, out var adminId))
            return B2BResponseFactory.Problem(context, 401, QuoteReasonCode.QuoteRequiredFieldMissing, "Auth required.");
        var r = await handler.HandleAsync(adminId, id, body ?? new SuspendCompanyRequest(null), ct);
        return Translate(context, r);
    }

    private static IResult Translate(HttpContext context, CompanyResult r)
    {
        if (r.IsSuccess)
        {
            return r.StatusCode == 201
                ? Results.Json(r.Body, statusCode: 201)
                : Results.Ok(r.Body);
        }
        return B2BResponseFactory.Problem(context, r.StatusCode, r.ReasonCode!.Value, "Company action rejected.");
    }
}

#endregion
