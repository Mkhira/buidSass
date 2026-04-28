# Quickstart: Support Tickets (Spec 023)

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Data Model**: [data-model.md](./data-model.md) · **Contract**: [contracts/support-tickets-contract.md](./contracts/support-tickets-contract.md)
**Module path**: `services/backend_api/Modules/Support/`
**Audience**: implementers picking up this slice after `/speckit-tasks` runs.

This guide gets you from "nothing" to "first end-to-end ticket flow with SLA-breach simulation passing in CI" in ~half a day.

---

## 0 · Prerequisites

| Requirement | Where to find |
|---|---|
| Spec 003 (modular monolith bootstrap, audit log, idempotency middleware, rate-limit middleware, storage abstraction) at DoD on `main` | `services/backend_api/Modules/Shared/` + `Modules/AuditLog/` |
| Spec 004 (identity + RBAC + customer account lifecycle subscriber) at DoD on `main` | `services/backend_api/Modules/Identity/` |
| Spec 015 (admin shell + idempotency + storage abstraction) at DoD on `main` (or contract-stubbed if not yet) | `services/backend_api/Modules/AdminFoundation/` |
| Cross-module read contracts: ship `Fake*` doubles in `Modules/Shared/Testing/` for any of 011 / 013 / 020 / 021 / 022 not yet at DoD | see Phase D in plan.md |
| Postgres 16 (Testcontainers in tests) | `services/backend_api/tests/Support.Tests/` |
| `FakeTimeProvider` for SLA-breach worker tests | `Microsoft.Extensions.TimeProvider.Testing` |

The 023 module DOES NOT depend on any UI; spec 014 / 015 build screens against this contract independently.

---

## 1 · First slice: open a ticket end-to-end

The hello-world walkthrough — implement just enough to get a ticket from POST → DB → audit row.

### 1.1 Wire the module

```csharp
// services/backend_api/Modules/Support/SupportModule.cs
public static IServiceCollection AddSupportModule(this IServiceCollection s, IConfiguration cfg)
{
    s.AddDbContext<SupportDbContext>(o => o
        .UseNpgsql(cfg.GetConnectionString("Default"))
        .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))); // project-memory rule

    s.AddMediatR(cfg2 => cfg2.RegisterServicesFromAssemblyContaining<SupportModule>());
    s.AddValidatorsFromAssembly(typeof(SupportModule).Assembly);

    // Subscribers
    s.AddScoped<IReturnOutcomeSubscriber, ReturnOutcomeHandler>();
    s.AddScoped<ICustomerAccountLifecycleSubscriber, CustomerAccountLifecycleHandler>();

    // Workers
    s.AddHostedService<SlaBreachWatchWorker>();
    s.AddHostedService<AutoCloseResolutionWindowWorker>();
    s.AddHostedService<OrphanedAssignmentReclaimWorker>();

    return s;
}
```

Register in `Program.cs`: `builder.Services.AddSupportModule(builder.Configuration);`

### 1.2 Migration

```bash
dotnet ef migrations add AddSupportSchema --project services/backend_api/Modules/Support
dotnet ef database update --project services/backend_api/Modules/Support
```

Verify: `psql -c "\dt support.*"` shows the 9 tables. The `BEFORE UPDATE OR DELETE` triggers on the 6 append-only tables are visible via `\d support.ticket_messages`.

### 1.3 Reference seeder

```bash
dotnet run --project services/backend_api -- seed --dataset=support-reference-data --mode=apply
```

Verify: `select * from support.sla_policies` shows 8 rows (4 priorities × 2 markets). `select * from support.support_market_schemas` shows 2 rows.

### 1.4 Implement the OpenTicket slice

```csharp
// Modules/Support/Customer/OpenTicket/OpenTicketCommand.cs
public sealed record OpenTicketCommand(
    Guid CustomerId,
    string Subject,
    string Body,
    string Category,
    string Priority,
    string Locale,
    string? LinkedEntityKind,
    Guid? LinkedEntityId,
    string[] AttachmentIds
) : IRequest<OpenTicketResult>;

public sealed record OpenTicketResult(
    Guid TicketId,
    string State,
    string MarketCode,
    Guid? AssignedAgentId,
    DateTimeOffset FirstResponseDueUtc,
    DateTimeOffset ResolutionDueUtc
);
```

Handler outline:

```csharp
public sealed class OpenTicketHandler : IRequestHandler<OpenTicketCommand, OpenTicketResult>
{
    private readonly SupportDbContext _db;
    private readonly IServiceProvider _sp;
    private readonly TimeProvider _clock;
    private readonly IAuditEventPublisher _audit;
    private readonly IPublisher _bus;
    // …

    public async Task<OpenTicketResult> Handle(OpenTicketCommand cmd, CancellationToken ct)
    {
        // 1. Validate inputs (FR-006, FR-007 category-kind consistency, FR-006 priority ∈ {low, normal})
        // 2. Resolve linked entity ownership + market_code via per-kind contract (FR-006a, FR-008)
        var (marketCode, vendorId, companyId) = await ResolveLinkedEntityOrCustomerOfRecord(cmd, ct);

        // 3. Snapshot SLA targets (FR-022)
        var sla = await _db.SlaPolicies.SingleAsync(p =>
            p.MarketCode == marketCode && p.Priority == cmd.Priority, ct);
        var now = _clock.GetUtcNow();

        // 4. Persist ticket
        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(),
            CustomerId = cmd.CustomerId,
            CompanyId = companyId,
            MarketCode = marketCode,
            Locale = cmd.Locale,
            Category = cmd.Category,
            Priority = cmd.Priority,
            State = TicketState.Open,
            Subject = cmd.Subject,
            Body = cmd.Body,
            LinkedEntityKind = cmd.LinkedEntityKind,
            LinkedEntityId = cmd.LinkedEntityId,
            VendorId = vendorId,
            FirstResponseTargetMinutesSnapshot = sla.FirstResponseTargetMinutes,
            ResolutionTargetMinutesSnapshot = sla.ResolutionTargetMinutes,
            FirstResponseDueUtc = now.AddMinutes(sla.FirstResponseTargetMinutes),
            ResolutionDueUtc = now.AddMinutes(sla.ResolutionTargetMinutes),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        _db.Tickets.Add(ticket);

        // 5. Persist initial customer message
        _db.TicketMessages.Add(new TicketMessage { /* customer_reply with body */ });

        // 6. Persist polymorphic link if any
        if (cmd.LinkedEntityKind is not null)
            _db.TicketLinks.Add(new TicketLink { /* … */ });

        // 7. Persist attachment metadata rows from the pre-uploaded ids
        foreach (var aid in cmd.AttachmentIds)
            _db.TicketAttachments.Add(new TicketAttachment { /* … */ });

        // 8. Audit + domain event
        await _audit.PublishAsync(AuditKinds.TicketOpened, new { ticket.Id, ticket.MarketCode, ticket.Category, ticket.Priority }, ct);

        await _db.SaveChangesAsync(ct);

        // 9. Auto-assign if enabled (FR-019)
        Guid? assignedAgentId = null;
        var marketSchema = await _db.SupportMarketSchemas.SingleAsync(s => s.MarketCode == marketCode, ct);
        if (marketSchema.AutoAssignmentEnabled && cmd.Category != "redaction_request")
            assignedAgentId = await TryAutoAssign(ticket.Id, marketCode, ct);

        await _bus.Publish(new TicketOpened(ticket.Id, ticket.CustomerId, marketCode, ticket.Category, ticket.Priority), ct);

        return new OpenTicketResult(ticket.Id, ticket.State, marketCode, assignedAgentId, ticket.FirstResponseDueUtc, ticket.ResolutionDueUtc);
    }
}
```

### 1.5 First contract test

```csharp
[Fact]
public async Task open_ticket_creates_row_in_open_state_and_emits_audit()
{
    using var f = new SupportTestFactory();
    var customerId = await f.SeedCustomerAsync();
    var orderLineId = await f.SeedDeliveredOrderLineAsync(customerId, market: "SA");

    var resp = await f.CustomerClient(customerId).PostAsJsonAsync("/v1/customer/support-tickets", new
    {
        subject = "Damaged unit",
        body = "Lot 442 arrived crushed.",
        category = "order_issue",
        priority = "normal",
        linked_entity_kind = "order_line",
        linked_entity_id = orderLineId,
        attachment_ids = Array.Empty<string>(),
        locale = "ar"
    }, headers: new { ["Idempotency-Key"] = Guid.NewGuid().ToString() });

    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    var body = await resp.Content.ReadFromJsonAsync<OpenTicketResult>();
    body.State.Should().Be("open");
    body.MarketCode.Should().Be("SA");

    var ticket = await f.Db.Tickets.SingleAsync(t => t.Id == body.TicketId);
    ticket.FirstResponseDueUtc.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(4), TimeSpan.FromSeconds(5));

    var auditRows = await f.AuditRowsForKindAsync("support.ticket.opened");
    auditRows.Should().ContainSingle(r => r.SubjectId == body.TicketId);
}
```

---

## 2 · Claim and reply: smoke

```csharp
[Fact]
public async Task agent_claims_ticket_and_first_reply_clears_first_response_due()
{
    using var f = new SupportTestFactory();
    var ticketId = await f.SeedOpenTicketAsync(market: "SA");

    var agent = await f.SeedAgentAsync(market: "SA");
    var resp = await f.AgentClient(agent.Id).PostAsync($"/v1/admin/support-tickets/{ticketId}/claim", body: null);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var ticket = await f.Db.Tickets.SingleAsync(t => t.Id == ticketId);
    ticket.State.Should().Be("in_progress");
    ticket.AssignedAgentId.Should().Be(agent.Id);

    // First agent reply
    await f.AgentClient(agent.Id).PostAsJsonAsync($"/v1/admin/support-tickets/{ticketId}/replies", new
    {
        body = "Could you share the expiry sticker photo?"
    });

    var t2 = await f.Db.Tickets.SingleAsync(t => t.Id == ticketId);
    t2.State.Should().Be("waiting_customer"); // because the agent reply asked for info
}
```

---

## 3 · SLA-breach simulation

```csharp
[Fact]
public async Task sla_breach_worker_emits_first_response_breach_within_60s_of_deadline()
{
    using var f = new SupportTestFactory();
    var clock = (FakeTimeProvider)f.Services.GetRequiredService<TimeProvider>();
    var ticketId = await f.SeedOpenTicketAsync(market: "SA", priority: "high"); // 1h first-response target

    // Simulate 65 minutes passing
    clock.Advance(TimeSpan.FromMinutes(65));

    // Run worker tick
    await f.RunSlaBreachWorkerTickAsync();

    // Assert: breach event row + acknowledgment stamp + emitted event
    var breach = await f.Db.TicketSlaBreachEvents.SingleAsync(b => b.TicketId == ticketId && b.BreachKind == "first_response");
    breach.Should().NotBeNull();
    var t = await f.Db.Tickets.SingleAsync(t => t.Id == ticketId);
    t.BreachAcknowledgedAtFirstResponse.Should().NotBeNull();

    // Idempotency: re-run worker; no duplicate
    await f.RunSlaBreachWorkerTickAsync();
    (await f.Db.TicketSlaBreachEvents.CountAsync(b => b.TicketId == ticketId && b.BreachKind == "first_response")).Should().Be(1);
}
```

---

## 4 · Conversion-to-return idempotency smoke

```csharp
[Fact]
public async Task convert_to_return_is_idempotent_on_idempotency_key()
{
    using var f = new SupportTestFactory();
    var ticketId = await f.SeedInProgressTicketAsync(category: "return_refund_request", linkedKind: "order_line");

    var key = Guid.NewGuid().ToString();
    var r1 = await f.CustomerClient(ownerOf: ticketId).PostAsJsonAsync(
        $"/v1/customer/support-tickets/{ticketId}/convert-to-return",
        body: new { },
        headers: new { ["Idempotency-Key"] = key });
    r1.StatusCode.Should().Be(HttpStatusCode.Created);

    // Retry with the same key
    var r2 = await f.CustomerClient(ownerOf: ticketId).PostAsJsonAsync(
        $"/v1/customer/support-tickets/{ticketId}/convert-to-return",
        body: new { },
        headers: new { ["Idempotency-Key"] = key });
    r2.StatusCode.Should().Be(HttpStatusCode.Created);

    // Exactly one return-request, exactly one TicketLink
    f.SpecThirteenFakeReturnContract.CreatedRequests.Should().HaveCount(1);
    (await f.Db.TicketLinks.CountAsync(l => l.TicketId == ticketId && l.Kind == "return_request")).Should().Be(1);
}
```

---

## 5 · Tests checklist (DoD-mapped)

| Test type | Coverage |
|---|---|
| Unit: `TicketStateMachine` invariants | property tests assert: no terminal→non-terminal except via reopen, no double-claim races inside the FSM, idempotent transitions |
| Unit: `MarketCodeResolver` (FR-006a) | linked-entity present + Available → linked entity's market; linked-entity Unavailable → throws `MarketCodeUnresolvable`; no linked entity → customer-of-record |
| Unit: SLA snapshot freezing | edits to `sla_policies` after ticket creation do NOT change the ticket's `*_target_minutes_snapshot` |
| Unit: reopen window math | within window + under cap → success; past window → reject; over cap → reject; market `reopen_window_days=0` → reject |
| Unit: profanity-of-display-handle | reused from spec 022; sanity test only |
| Unit: reason-code mapper | every owned reason code has both AR + EN ICU keys |
| Integration: every customer slice | Acceptance Scenarios 1–5 from US-1 / US-2 / US-3 / US-6 |
| Integration: every agent slice | Acceptance Scenarios from US-2 / US-4 / US-5 |
| Integration: SLA-breach worker | SC-005 latency + SC-006 idempotency under 100-iteration repeat |
| Integration: claim concurrency | SC-007 — 100 concurrent claims, exactly 1 winner |
| Integration: conversion idempotency | SC-010 — 100-iteration retry produces exactly 1 return-request + 1 link |
| Integration: subscriber tests | `return.completed` → originating ticket auto-resolves; `customer.account_locked` → tickets auto-close |
| Integration: redaction paths | super_admin redact attachment + redact message; customer-facing read returns tombstone; audit row written |
| Integration: queue perf | SC-011 — 50-row page in < 500 ms p95 against 10 000 seeded tickets |
| Integration: leak detection | SC-004 — exhaustive scan of customer-facing read endpoints; no `internal_note` leaks |
| Contract tests | every spec.md Acceptance Scenario asserted against live handler |

---

## 6 · DoD checklist

- [ ] All 9 tables created via single migration; append-only triggers tested.
- [ ] `SupportReferenceDataSeeder` seeds 8 SLA-policy rows + 2 market schemas idempotently across all environments.
- [ ] `SupportV1DevSeeder` seeds ≥ 1 ticket per state × 10 categories + breach + conversion + redaction-request examples (Dev + Staging only).
- [ ] All 30 endpoints implemented with role-gated permissions, idempotency middleware applied where required.
- [ ] All 16 domain events emit on the corresponding lifecycle transitions; spec 025 subscriber binding deferred to its own PR.
- [ ] All cross-module shared interfaces declared in `Modules/Shared/`; fake doubles in `Modules/Shared/Testing/`.
- [ ] `support.en.icu` + `support.ar.icu` carry every system-generated string + reason code; AR strings flagged in `AR_EDITORIAL_REVIEW.md`.
- [ ] Audit-coverage script (spec 015) reports 100 % coverage for the 18 audit-event kinds.
- [ ] All workers run idempotently under repeated-tick stress tests.
- [ ] OpenAPI regenerated (`openapi.support.json` checked in).
- [ ] Constitution / ADR fingerprint included on the PR.
- [ ] Lint + format + contract-diff checks pass.
- [ ] CodeRabbit review feedback addressed (fix Critical / Major; stop at nit-only round per project-memory rule).

---

## 7 · Common pitfalls (project-memory)

- **`ManyServiceProvidersCreatedWarning`**: every new module's `AddDbContext` MUST suppress this warning or Identity tests break across the whole solution. See project memory.
- **Cross-module hooks via `Modules/Shared/`**: NEVER reference another module directly from `Modules/Support/`; always declare the interface in `Modules/Shared/` and let the owning module bind it. Avoids module dependency cycles.
- **Gate Secure cookie on `Request.IsHttps`**: cookies with `Secure=true` don't round-trip in `WebApplicationFactory` HTTP tests. The 023 module doesn't issue cookies, but if any auth integration test issues one, gate it on `IsHttps`.
- **CodeRabbit iteration**: fix Critical/Major; stop at nit-only or 1-Minor round; budget 3-4 rounds.
