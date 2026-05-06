using BackendApi.Features.Seeding;
using BackendApi.Modules.Support.Entities;
using BackendApi.Modules.Support.Persistence;
using BackendApi.Modules.Support.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BackendApi.Modules.Support.Seeding;

/// <summary>
/// Spec 023 US7 (T111-T115) — dev / staging-only synthetic dataset spanning
/// every ticket state, all 10 categories, and the headline operational paths
/// (SLA breach, return-conversion, redaction-request). Powers QA + training
/// + storefront/admin UI development without requiring agent workflow drives.
///
/// <para>Hard-gated: skips when the host environment is anything other than
/// Development or Staging (SeedGuard short-circuits Production additionally).
/// Idempotent — re-runs no-op once the synthetic ticket ids exist.</para>
///
/// <para>State + category coverage per spec.md SC-009:</para>
/// <list type="bullet">
///   <item>10 <b>open</b> tickets — one per FR-007 category</item>
///   <item>5 <b>in_progress</b> tickets — assigned to synthetic agents</item>
///   <item>4 <b>waiting_customer</b> tickets — agent posted a clarification reply</item>
///   <item>6 <b>resolved</b> tickets — 3 within reopen window, 3 past it</item>
///   <item>3 <b>closed</b> tickets — auto-close + lead-force-close + customer-account-locked</item>
///   <item>2 active SLA breach examples (first-response + resolution)</item>
///   <item>2 return-conversion examples (with bidirectional TicketLink)</item>
///   <item>2 review-dispute escalations</item>
///   <item>1 redaction-request ticket</item>
/// </list>
///
/// <para>Bilingual subjects + bodies use editorial-grade Arabic + English
/// (Principle 4); machine-translated artifacts MUST NOT appear.</para>
/// </summary>
public sealed class SupportV1DevSeeder : ISeeder
{
    public string Name => "support.v1-dev-data";
    public int Version => 1;
    public IReadOnlyList<string> DependsOn => new[] { "support.reference-data" };

    // Pinned UTC timestamp so re-runs produce identical rows.
    private static readonly DateTimeOffset BaseTime =
        new(2026, 4, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Guid SystemActor = Guid.Empty;

    public async Task ApplyAsync(SeedContext ctx, CancellationToken ct)
    {
        if (!ctx.Env.IsDevelopment() && !ctx.Env.IsStaging())
        {
            return;
        }

        var db = ctx.Services.GetRequiredService<SupportDbContext>();

        var schemaSeeded = await db.MarketSchemas.AsNoTracking()
            .AnyAsync(s => s.MarketCode == "SA", ct);
        if (!schemaSeeded)
        {
            // Reference data hasn't run yet — bail rather than create tickets
            // whose SLA snapshots reference non-existent policies.
            return;
        }

        var fixtures = BuildFixtures();

        var fixtureIds = fixtures.Select(f => f.Ticket.Id).ToList();
        var existing = await db.Tickets.AsNoTracking()
            .Where(t => fixtureIds.Contains(t.Id))
            .Select(t => t.Id)
            .ToListAsync(ct);
        var existingSet = existing.ToHashSet();

        foreach (var fx in fixtures)
        {
            if (existingSet.Contains(fx.Ticket.Id)) continue;

            db.Tickets.Add(fx.Ticket);
            foreach (var msg in fx.Messages) db.Messages.Add(msg);
            foreach (var lnk in fx.Links) db.Links.Add(lnk);
            foreach (var asg in fx.Assignments) db.Assignments.Add(asg);
            foreach (var brc in fx.BreachEvents) db.SlaBreachEvents.Add(brc);
        }

        await db.SaveChangesAsync(ct);
    }

    private static IReadOnlyList<Fixture> BuildFixtures()
    {
        var list = new List<Fixture>();
        var ticketIndex = 0;

        // One open ticket per FR-007 category (per TicketCategoryNames.All — 11 entries),
        // alternating SA / EG. Last entry is the redaction-request example.
        var categories = TicketCategoryNames.All;

        foreach (var cat in categories)
        {
            var market = ticketIndex % 2 == 0 ? "SA" : "EG";
            var locale = ticketIndex % 2 == 0 ? "ar" : "en";
            list.Add(BuildOpenTicket(ticketIndex++, cat, market, locale));
        }

        // 5 in_progress — three agents, mix of priorities.
        for (var i = 0; i < 5; i++)
        {
            list.Add(BuildInProgressTicket(ticketIndex++, market: i % 2 == 0 ? "SA" : "EG"));
        }

        // 4 waiting_customer — agent has posted a clarifying reply.
        for (var i = 0; i < 4; i++)
        {
            list.Add(BuildWaitingCustomerTicket(ticketIndex++, market: i % 2 == 0 ? "SA" : "EG"));
        }

        // 6 resolved — 3 within reopen window (resolved 5 days ago), 3 past it (resolved 30 days ago).
        for (var i = 0; i < 3; i++)
        {
            list.Add(BuildResolvedTicket(ticketIndex++, withinWindow: true,
                market: i % 2 == 0 ? "SA" : "EG"));
        }
        for (var i = 0; i < 3; i++)
        {
            list.Add(BuildResolvedTicket(ticketIndex++, withinWindow: false,
                market: i % 2 == 0 ? "SA" : "EG"));
        }

        // 3 closed — auto-close, lead-force-close, account-locked.
        list.Add(BuildClosedTicket(ticketIndex++, TicketTriggerKind.AutoCloseResolutionWindow, "SA"));
        list.Add(BuildClosedTicket(ticketIndex++, TicketTriggerKind.LeadForceClose, "EG"));
        list.Add(BuildClosedTicket(ticketIndex++, TicketTriggerKind.AuthorAccountLocked, "SA"));

        // 2 active SLA breach examples — one first-response, one resolution.
        list.Add(BuildBreachExample(ticketIndex++, TicketSlaBreachKind.FirstResponse, "SA"));
        list.Add(BuildBreachExample(ticketIndex++, TicketSlaBreachKind.Resolution, "EG"));

        // 2 return-conversion examples (forward + reverse linkage demoed).
        list.Add(BuildReturnConversionExample(ticketIndex++, "SA"));
        list.Add(BuildReturnConversionExample(ticketIndex++, "EG"));

        // 2 review-dispute escalations.
        list.Add(BuildReviewDisputeExample(ticketIndex++, "SA"));
        list.Add(BuildReviewDisputeExample(ticketIndex++, "EG"));

        return list;
    }

    private static Fixture BuildOpenTicket(int idx, string category, string market, string locale)
    {
        var ticketId = SyntheticGuid("open", idx);
        var customerId = SyntheticGuid("customer", idx);
        var createdAt = BaseTime.AddHours(idx);

        var (subject, body) = SampleSubjectBody(category, locale);

        var ticket = new SupportTicket
        {
            Id = ticketId,
            CustomerId = customerId,
            MarketCode = market,
            Locale = locale,
            Category = category,
            Priority = TicketPriorityNames.Normal,
            State = TicketStateNames.Open,
            Subject = subject,
            Body = body,
            FirstResponseTargetMinutesSnapshot = 240,
            ResolutionTargetMinutesSnapshot = 2880,
            FirstResponseDueUtc = createdAt.AddMinutes(240),
            ResolutionDueUtc = createdAt.AddMinutes(2880),
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = createdAt,
        };

        return new Fixture
        {
            Ticket = ticket,
            Messages = new List<TicketMessage>
            {
                BuildSystemEvent(ticketId, createdAt, locale, "Ticket opened."),
            },
            Links = new List<TicketLink>(),
            Assignments = new List<TicketAssignment>(),
            BreachEvents = new List<TicketSlaBreachEvent>(),
        };
    }

    private static Fixture BuildInProgressTicket(int idx, string market)
    {
        var ticketId = SyntheticGuid("inprogress", idx);
        var customerId = SyntheticGuid("customer-inprogress", idx);
        var agentId = SyntheticGuid("agent", idx % 3);
        var createdAt = BaseTime.AddDays(1).AddHours(idx);
        var claimedAt = createdAt.AddMinutes(15);
        var locale = market == "SA" ? "ar" : "en";

        var (subject, body) = SampleSubjectBody(TicketCategoryNames.OrderIssue, locale);

        var ticket = new SupportTicket
        {
            Id = ticketId,
            CustomerId = customerId,
            MarketCode = market,
            Locale = locale,
            Category = TicketCategoryNames.OrderIssue,
            Priority = TicketPriorityNames.Normal,
            State = TicketStateNames.InProgress,
            Subject = subject,
            Body = body,
            AssignedAgentId = agentId,
            FirstResponseTargetMinutesSnapshot = 240,
            ResolutionTargetMinutesSnapshot = 2880,
            FirstResponseDueUtc = createdAt.AddMinutes(240),
            ResolutionDueUtc = createdAt.AddMinutes(2880),
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = claimedAt,
        };

        return new Fixture
        {
            Ticket = ticket,
            Messages = new List<TicketMessage>
            {
                BuildSystemEvent(ticketId, createdAt, locale, "Ticket opened."),
                BuildSystemEvent(ticketId, claimedAt, locale, "Agent claimed."),
            },
            Links = new List<TicketLink>(),
            Assignments = new List<TicketAssignment>
            {
                new()
                {
                    Id = SyntheticGuid("assign", idx),
                    TicketId = ticketId,
                    AgentId = agentId,
                    AssignmentKind = TicketAssignmentKind.SelfClaim,
                    AssignedByActorId = agentId,
                    AssignedAtUtc = claimedAt,
                },
            },
            BreachEvents = new List<TicketSlaBreachEvent>(),
        };
    }

    private static Fixture BuildWaitingCustomerTicket(int idx, string market)
    {
        var ticketId = SyntheticGuid("waitingcustomer", idx);
        var customerId = SyntheticGuid("customer-waiting", idx);
        var agentId = SyntheticGuid("agent-waiting", idx);
        var createdAt = BaseTime.AddDays(2).AddHours(idx);
        var claimedAt = createdAt.AddMinutes(20);
        var repliedAt = claimedAt.AddMinutes(30);
        var locale = market == "SA" ? "ar" : "en";
        var (subject, body) = SampleSubjectBody(TicketCategoryNames.PaymentIssue, locale);

        var ticket = new SupportTicket
        {
            Id = ticketId,
            CustomerId = customerId,
            MarketCode = market,
            Locale = locale,
            Category = TicketCategoryNames.PaymentIssue,
            Priority = TicketPriorityNames.Normal,
            State = TicketStateNames.WaitingCustomer,
            Subject = subject,
            Body = body,
            AssignedAgentId = agentId,
            FirstResponseTargetMinutesSnapshot = 240,
            ResolutionTargetMinutesSnapshot = 2880,
            FirstResponseDueUtc = createdAt.AddMinutes(240),
            ResolutionDueUtc = createdAt.AddMinutes(2880),
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = repliedAt,
        };

        return new Fixture
        {
            Ticket = ticket,
            Messages = new List<TicketMessage>
            {
                BuildSystemEvent(ticketId, createdAt, locale, "Ticket opened."),
                new()
                {
                    Id = SyntheticGuid("msg-agent-reply", idx),
                    TicketId = ticketId,
                    Kind = TicketMessageKindNames.AgentReply,
                    ActorId = agentId,
                    ActorRole = TicketActorKindNames.Agent,
                    Body = locale == "ar"
                        ? "هل يمكنك تزويدنا برقم الطلب من فضلك؟"
                        : "Could you share the order reference please?",
                    BodyLocale = locale,
                    LeadIntervention = false,
                    CreatedAtUtc = repliedAt,
                },
            },
            Links = new List<TicketLink>(),
            Assignments = new List<TicketAssignment>
            {
                new()
                {
                    Id = SyntheticGuid("assign-waiting", idx),
                    TicketId = ticketId,
                    AgentId = agentId,
                    AssignmentKind = TicketAssignmentKind.SelfClaim,
                    AssignedByActorId = agentId,
                    AssignedAtUtc = claimedAt,
                },
            },
            BreachEvents = new List<TicketSlaBreachEvent>(),
        };
    }

    private static Fixture BuildResolvedTicket(int idx, bool withinWindow, string market)
    {
        var ticketId = SyntheticGuid(withinWindow ? "resolved-w" : "resolved-p", idx);
        var customerId = SyntheticGuid("customer-resolved", idx);
        var agentId = SyntheticGuid("agent-resolved", idx);
        var resolvedAt = withinWindow
            ? BaseTime.AddDays(28) // within 14-day reopen window from BaseTime+30d
            : BaseTime.AddDays(1); // > 14 days behind any "now()" near BaseTime+30d
        var createdAt = resolvedAt.AddHours(-4);
        var locale = market == "SA" ? "ar" : "en";
        var (subject, body) = SampleSubjectBody(TicketCategoryNames.GeneralQuestion, locale);

        return new Fixture
        {
            Ticket = new SupportTicket
            {
                Id = ticketId,
                CustomerId = customerId,
                MarketCode = market,
                Locale = locale,
                Category = TicketCategoryNames.GeneralQuestion,
                Priority = TicketPriorityNames.Normal,
                State = TicketStateNames.Resolved,
                Subject = subject,
                Body = body,
                AssignedAgentId = agentId,
                FirstResponseTargetMinutesSnapshot = 240,
                ResolutionTargetMinutesSnapshot = 2880,
                FirstResponseDueUtc = createdAt.AddMinutes(240),
                ResolutionDueUtc = createdAt.AddMinutes(2880),
                ResolvedAtUtc = resolvedAt,
                CreatedAtUtc = createdAt,
                UpdatedAtUtc = resolvedAt,
            },
            Messages = new List<TicketMessage>
            {
                BuildSystemEvent(ticketId, createdAt, locale, "Ticket opened."),
                BuildSystemEvent(ticketId, resolvedAt, locale, "Ticket resolved."),
            },
            Links = new List<TicketLink>(),
            Assignments = new List<TicketAssignment>
            {
                new()
                {
                    Id = SyntheticGuid("assign-resolved", idx),
                    TicketId = ticketId,
                    AgentId = agentId,
                    AssignmentKind = TicketAssignmentKind.SelfClaim,
                    AssignedByActorId = agentId,
                    AssignedAtUtc = createdAt.AddMinutes(20),
                },
            },
            BreachEvents = new List<TicketSlaBreachEvent>(),
        };
    }

    private static Fixture BuildClosedTicket(int idx, string trigger, string market)
    {
        var ticketId = SyntheticGuid("closed", idx);
        var customerId = SyntheticGuid("customer-closed", idx);
        var closedAt = BaseTime.AddDays(20).AddHours(idx);
        var createdAt = closedAt.AddDays(-10);
        var locale = market == "SA" ? "ar" : "en";
        var (subject, body) = SampleSubjectBody(TicketCategoryNames.GeneralQuestion, locale);

        return new Fixture
        {
            Ticket = new SupportTicket
            {
                Id = ticketId,
                CustomerId = customerId,
                MarketCode = market,
                Locale = locale,
                Category = TicketCategoryNames.GeneralQuestion,
                Priority = TicketPriorityNames.Normal,
                State = TicketStateNames.Closed,
                Subject = subject,
                Body = body,
                FirstResponseTargetMinutesSnapshot = 240,
                ResolutionTargetMinutesSnapshot = 2880,
                FirstResponseDueUtc = createdAt.AddMinutes(240),
                ResolutionDueUtc = createdAt.AddMinutes(2880),
                ClosedAtUtc = closedAt,
                CreatedAtUtc = createdAt,
                UpdatedAtUtc = closedAt,
            },
            Messages = new List<TicketMessage>
            {
                BuildSystemEvent(ticketId, createdAt, locale, "Ticket opened."),
                BuildSystemEvent(ticketId, closedAt, locale, $"Closed via {trigger}."),
            },
            Links = new List<TicketLink>(),
            Assignments = new List<TicketAssignment>(),
            BreachEvents = new List<TicketSlaBreachEvent>(),
        };
    }

    private static Fixture BuildBreachExample(int idx, string breachKind, string market)
    {
        var ticketId = SyntheticGuid("breach", idx);
        var customerId = SyntheticGuid("customer-breach", idx);
        var createdAt = BaseTime.AddDays(5).AddHours(idx);
        var detectedAt = createdAt.AddHours(6);
        var locale = market == "SA" ? "ar" : "en";
        var (subject, body) = SampleSubjectBody(TicketCategoryNames.OrderIssue, locale);

        var ticket = new SupportTicket
        {
            Id = ticketId,
            CustomerId = customerId,
            MarketCode = market,
            Locale = locale,
            Category = TicketCategoryNames.OrderIssue,
            Priority = TicketPriorityNames.High,
            State = TicketStateNames.Open,
            Subject = subject,
            Body = body,
            FirstResponseTargetMinutesSnapshot = 60,
            ResolutionTargetMinutesSnapshot = 720,
            FirstResponseDueUtc = createdAt.AddMinutes(60),
            ResolutionDueUtc = createdAt.AddMinutes(720),
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = detectedAt,
        };

        if (breachKind == TicketSlaBreachKind.FirstResponse)
        {
            ticket.BreachAcknowledgedAtFirstResponse = detectedAt;
        }
        else
        {
            ticket.BreachAcknowledgedAtResolution = detectedAt;
        }

        return new Fixture
        {
            Ticket = ticket,
            Messages = new List<TicketMessage>
            {
                BuildSystemEvent(ticketId, createdAt, locale, "Ticket opened."),
                BuildSystemEvent(ticketId, detectedAt, locale, $"SLA {breachKind} breach detected."),
            },
            Links = new List<TicketLink>(),
            Assignments = new List<TicketAssignment>(),
            BreachEvents = new List<TicketSlaBreachEvent>
            {
                new()
                {
                    Id = SyntheticGuid("breach-event", idx),
                    TicketId = ticketId,
                    BreachKind = breachKind,
                    DetectedAtUtc = detectedAt,
                    TargetDueUtc = breachKind == TicketSlaBreachKind.FirstResponse
                        ? ticket.FirstResponseDueUtc
                        : ticket.ResolutionDueUtc,
                    EventEmitted = true,
                },
            },
        };
    }

    private static Fixture BuildReturnConversionExample(int idx, string market)
    {
        var ticketId = SyntheticGuid("returnconv", idx);
        var customerId = SyntheticGuid("customer-returnconv", idx);
        var returnRequestId = SyntheticGuid("returnrequest", idx);
        var orderLineId = SyntheticGuid("orderline", idx);
        var createdAt = BaseTime.AddDays(8).AddHours(idx);
        var convertedAt = createdAt.AddHours(2);
        var locale = market == "SA" ? "ar" : "en";
        var (subject, body) = SampleSubjectBody(TicketCategoryNames.ReturnRefundRequest, locale);

        return new Fixture
        {
            Ticket = new SupportTicket
            {
                Id = ticketId,
                CustomerId = customerId,
                MarketCode = market,
                Locale = locale,
                Category = TicketCategoryNames.ReturnRefundRequest,
                Priority = TicketPriorityNames.Normal,
                State = TicketStateNames.WaitingCustomer,
                Subject = subject,
                Body = body,
                LinkedEntityKind = TicketLinkedEntityKindNames.OrderLine,
                LinkedEntityId = orderLineId,
                FirstResponseTargetMinutesSnapshot = 240,
                ResolutionTargetMinutesSnapshot = 2880,
                FirstResponseDueUtc = createdAt.AddMinutes(240),
                ResolutionDueUtc = createdAt.AddMinutes(2880),
                CreatedAtUtc = createdAt,
                UpdatedAtUtc = convertedAt,
            },
            Messages = new List<TicketMessage>
            {
                BuildSystemEvent(ticketId, createdAt, locale, "Ticket opened."),
                BuildSystemEvent(ticketId, convertedAt, locale, "Converted to return-request."),
            },
            Links = new List<TicketLink>
            {
                new()
                {
                    Id = SyntheticGuid("link-orderline", idx),
                    TicketId = ticketId,
                    Kind = TicketLinkedEntityKindNames.OrderLine,
                    LinkedEntityId = orderLineId,
                    CreatedVia = TicketLinkCreatedVia.Submission,
                    CreatedAtUtc = createdAt,
                },
                new()
                {
                    Id = SyntheticGuid("link-returnrequest", idx),
                    TicketId = ticketId,
                    Kind = TicketLinkedEntityKindNames.ReturnRequest,
                    LinkedEntityId = returnRequestId,
                    CreatedVia = TicketLinkCreatedVia.Conversion,
                    IdempotencyKey = $"convert-{ticketId:N}",
                    CreatedAtUtc = convertedAt,
                },
            },
            Assignments = new List<TicketAssignment>(),
            BreachEvents = new List<TicketSlaBreachEvent>(),
        };
    }

    private static Fixture BuildReviewDisputeExample(int idx, string market)
    {
        var ticketId = SyntheticGuid("reviewdispute", idx);
        var customerId = SyntheticGuid("customer-reviewdispute", idx);
        var reviewId = SyntheticGuid("review", idx);
        var createdAt = BaseTime.AddDays(10).AddHours(idx);
        var locale = market == "SA" ? "ar" : "en";
        var subject = locale == "ar"
            ? "اعتراض على إخفاء مراجعة"
            : "Dispute: review hidden incorrectly";
        var body = locale == "ar"
            ? "أعتقد أن المراجعة لا تخالف السياسة. الرجاء المراجعة."
            : "I believe my review does not violate the policy; please re-review.";

        return new Fixture
        {
            Ticket = new SupportTicket
            {
                Id = ticketId,
                CustomerId = customerId,
                MarketCode = market,
                Locale = locale,
                Category = TicketCategoryNames.GeneralQuestion,
                Priority = TicketPriorityNames.Normal,
                State = TicketStateNames.Open,
                Subject = subject,
                Body = body,
                LinkedEntityKind = TicketLinkedEntityKindNames.Review,
                LinkedEntityId = reviewId,
                FirstResponseTargetMinutesSnapshot = 240,
                ResolutionTargetMinutesSnapshot = 2880,
                FirstResponseDueUtc = createdAt.AddMinutes(240),
                ResolutionDueUtc = createdAt.AddMinutes(2880),
                CreatedAtUtc = createdAt,
                UpdatedAtUtc = createdAt,
            },
            Messages = new List<TicketMessage>
            {
                BuildSystemEvent(ticketId, createdAt, locale, "Ticket opened."),
            },
            Links = new List<TicketLink>
            {
                new()
                {
                    Id = SyntheticGuid("link-review", idx),
                    TicketId = ticketId,
                    Kind = TicketLinkedEntityKindNames.Review,
                    LinkedEntityId = reviewId,
                    CreatedVia = TicketLinkCreatedVia.Submission,
                    CreatedAtUtc = createdAt,
                },
            },
            Assignments = new List<TicketAssignment>(),
            BreachEvents = new List<TicketSlaBreachEvent>(),
        };
    }

    private static (string Subject, string Body) SampleSubjectBody(string category, string locale)
    {
        // Editorial-grade Arabic + English samples per Principle 4. Each entry
        // is reviewed in AR_EDITORIAL_REVIEW.md (T143/T144); machine-translation
        // artifacts MUST NOT appear here.
        return (category, locale) switch
        {
            (TicketCategoryNames.OrderIssue, "ar") =>
                ("لم يصل الطلب رقم 1234", "مر أكثر من خمسة أيام ولم يصل الطلب بعد."),
            (TicketCategoryNames.OrderIssue, _) =>
                ("Order #1234 hasn't arrived", "It has been more than five days since the order was placed."),
            (TicketCategoryNames.PaymentIssue, "ar") =>
                ("خصم مزدوج على البطاقة", "تم خصم المبلغ مرتين عند إتمام عملية الدفع."),
            (TicketCategoryNames.PaymentIssue, _) =>
                ("Card was charged twice", "The amount was deducted twice during checkout."),
            (TicketCategoryNames.ShippingIssue, "ar") =>
                ("تتبع الشحنة لا يعمل", "رابط التتبع يعرض رسالة خطأ."),
            (TicketCategoryNames.ShippingIssue, _) =>
                ("Shipment tracking not working", "The tracking link returns an error message."),
            (TicketCategoryNames.ProductDefect, "ar") =>
                ("عيب في المنتج", "المنتج وصل به كسر واضح في الجزء العلوي."),
            (TicketCategoryNames.ProductDefect, _) =>
                ("Product defect", "The product arrived with a visible crack on the top."),
            (TicketCategoryNames.ReturnRefundRequest, "ar") =>
                ("طلب إرجاع منتج", "المنتج مختلف عن الموجود في الصور."),
            (TicketCategoryNames.ReturnRefundRequest, _) =>
                ("Return request", "The product differs from what is shown in the photos."),
            (TicketCategoryNames.QuoteQuestion, "ar") =>
                ("استفسار عن عرض سعر", "أحتاج تسعيرة بالجملة لفئة منتجات معينة."),
            (TicketCategoryNames.QuoteQuestion, _) =>
                ("Quote inquiry", "I need a wholesale price quote for a specific product category."),
            (TicketCategoryNames.AccountVerification, "ar") =>
                ("تحقق من الحساب المهني", "أرغب في رفع وثيقة التسجيل التجاري."),
            (TicketCategoryNames.AccountVerification, _) =>
                ("Professional account verification", "I would like to upload my commercial-registration document."),
            (TicketCategoryNames.GeneralQuestion, "ar") =>
                ("سؤال عام عن المتجر", "ما هي أوقات العمل لخدمة العملاء؟"),
            (TicketCategoryNames.GeneralQuestion, _) =>
                ("General question", "What are customer-service operating hours?"),
            (TicketCategoryNames.ReviewDispute, "ar") =>
                ("اعتراض على إخفاء مراجعة", "أعتقد أن مراجعتي لا تخالف السياسة."),
            (TicketCategoryNames.ReviewDispute, _) =>
                ("Review dispute", "I believe my review does not violate the policy."),
            (TicketCategoryNames.VerificationQuery, "ar") =>
                ("استفسار عن حالة التحقق", "تم رفض ملف التحقق دون توضيح السبب."),
            (TicketCategoryNames.VerificationQuery, _) =>
                ("Verification status", "My verification submission was rejected without a clear reason."),
            (TicketCategoryNames.RedactionRequest, "ar") =>
                ("طلب إخفاء بيانات", "أرغب في حذف رقم الهاتف من إحدى رسائلي السابقة."),
            (TicketCategoryNames.RedactionRequest, _) =>
                ("Redaction request", "Please redact the phone number from one of my prior messages."),
            _ => ("Sample", "Sample body."),
        };
    }

    private static TicketMessage BuildSystemEvent(
        Guid ticketId, DateTimeOffset at, string locale, string body) =>
        new()
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Kind = TicketMessageKindNames.SystemEvent,
            ActorId = null,
            ActorRole = TicketActorKindNames.System,
            Body = body,
            BodyLocale = locale,
            LeadIntervention = false,
            CreatedAtUtc = at,
        };

    /// <summary>
    /// Deterministic GUID generator. Using a hash of (label, idx) keeps the
    /// ids stable across runs so idempotency-by-id holds and downstream
    /// fixtures (UI screenshots, test references) can pin to known ids.
    /// </summary>
    private static Guid SyntheticGuid(string label, int idx)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"support-v1:{label}:{idx}"));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        // Force version 4 + variant per RFC 4122 so Postgres treats it as a
        // proper uuid.
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x40);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    private sealed class Fixture
    {
        public required SupportTicket Ticket { get; init; }
        public required List<TicketMessage> Messages { get; init; }
        public required List<TicketLink> Links { get; init; }
        public required List<TicketAssignment> Assignments { get; init; }
        public required List<TicketSlaBreachEvent> BreachEvents { get; init; }
    }
}
