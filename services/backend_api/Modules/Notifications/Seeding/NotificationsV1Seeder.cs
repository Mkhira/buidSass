using BackendApi.Modules.Notifications.Domain;
using BackendApi.Modules.Notifications.Persistence;
using BackendApi.Modules.Notifications.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Notifications.Seeding;

/// <summary>
/// T059 — V1 seed data: a handful of template + template-version rows
/// covering the launch transactional event-kind set, plus a sample campaign.
/// All AR copy is placeholder pending T058 human editorial sign-off — every
/// seeded TemplateVersion ships with <c>ArEditorialReviewed=false</c>. The
/// CompanionAR-review checklist (<c>ar-editorial-review.md</c>) enumerates
/// exactly which rows need a reviewer pass before production launch.
///
/// Idempotent — seeds only if the table is empty so re-running on a populated
/// DB is a no-op.
/// </summary>
public sealed class NotificationsV1Seeder
{
    private readonly NotificationsDbContext _db;
    private readonly ILogger<NotificationsV1Seeder> _logger;
    private readonly TimeProvider _clock;

    private static readonly Guid SeedAuthorId = new("00000000-0000-0000-0000-00000000aaaa");

    private static readonly (string EventKind, string SubjectAr, string SubjectEn, string BodyAr, string BodyEn)[] Templates =
    {
        ("auth.otp_requested",
         "رمز التحقق الخاص بك",
         "Your verification code",
         "رمز التحقق الخاص بك هو {{code}} وسينتهي خلال {{ttl_minutes}} دقيقة.",
         "Your verification code is {{code}}. It expires in {{ttl_minutes}} minutes."),
        ("order.placed",
         "تم استلام طلبك {{order_number}}",
         "Your order {{order_number}} has been placed",
         "شكرًا لطلبك. سنرسل لك التحديثات هنا.",
         "Thank you for your order. We'll keep you posted here."),
        ("order.confirmed",
         "تأكيد الطلب {{order_number}}",
         "Order {{order_number}} confirmed",
         "تم تأكيد طلبك. الإجمالي {{total}} {{currency}}.",
         "Your order has been confirmed. Total {{total}} {{currency}}."),
        ("order.shipped",
         "تم شحن طلبك {{order_number}}",
         "Your order {{order_number}} has shipped",
         "شركة الشحن {{carrier}}، رقم التتبع {{tracking}}.",
         "Carrier: {{carrier}}, tracking: {{tracking}}."),
        ("order.delivered",
         "تم تسليم طلبك {{order_number}}",
         "Order {{order_number}} delivered",
         "تم تسليم طلبك. نأمل أن تكون راضيًا!",
         "Your order has been delivered. We hope you're happy with it!"),
        ("order.cancelled",
         "تم إلغاء الطلب {{order_number}}",
         "Order {{order_number}} cancelled",
         "تم إلغاء طلبك. السبب: {{reason}}.",
         "Your order has been cancelled. Reason: {{reason}}."),
        ("order.refund_initiated",
         "بدأ استرداد {{amount}} {{currency}}",
         "Refund of {{amount}} {{currency}} initiated",
         "بدأت معالجة استرداد {{amount}} {{currency}} لطلبك {{order_number}}.",
         "We've started a refund of {{amount}} {{currency}} for order {{order_number}}."),
        ("order.refund_completed",
         "اكتمل استرداد {{amount}} {{currency}}",
         "Refund of {{amount}} {{currency}} completed",
         "تم استرداد {{amount}} {{currency}} لطلبك {{order_number}}.",
         "Refund of {{amount}} {{currency}} for order {{order_number}} completed."),
        ("verification.approved",
         "تمت الموافقة على التحقق",
         "Verification approved",
         "تم التحقق من حسابك بنجاح.",
         "Your account verification has been approved."),
        ("verification.rejected",
         "تم رفض التحقق",
         "Verification rejected",
         "للأسف لم نتمكن من التحقق من حسابك. السبب: {{reason}}.",
         "We could not verify your account. Reason: {{reason}}."),
        ("pricing.price_dropped",
         "انخفض سعر {{product_name}}",
         "Price drop for {{product_name}}",
         "{{product_name}} متاح الآن بـ {{new_price}} {{currency}} (السعر السابق {{old_price}}).",
         "{{product_name}} is now {{new_price}} {{currency}} (was {{old_price}})."),
        ("inventory.restocked",
         "{{product_name}} متاح مرة أخرى",
         "{{product_name}} is back in stock",
         "المنتج الذي تتابعه عاد للمخزون.",
         "The product you've been watching is back in stock."),
        ("cart.abandoned_24h",
         "هل نسيت شيئًا في سلتك؟",
         "Did you forget something in your cart?",
         "لا تزال {{item_count}} منتجات بقيمة {{cart_total}} {{currency}} في سلتك.",
         "{{item_count}} items totaling {{cart_total}} {{currency}} are still in your cart."),
        ("shipping.status_changed",
         "تحديث شحن للطلب {{order_number}}",
         "Shipping update for order {{order_number}}",
         "حالة الشحن: {{status}}.",
         "Shipping status: {{status}}."),
    };

    public NotificationsV1Seeder(
        NotificationsDbContext db,
        ILogger<NotificationsV1Seeder> logger,
        TimeProvider clock)
    {
        _db = db;
        _logger = logger;
        _clock = clock;
    }

    public async Task SeedAsync(CancellationToken ct)
    {
        if (await _db.Templates.AnyAsync(ct))
        {
            _logger.LogInformation("NotificationsV1Seeder: templates table is non-empty, skipping.");
            return;
        }

        var now = _clock.GetUtcNow();
        foreach (var t in Templates)
        {
            var template = new Template
            {
                Id = Guid.NewGuid(),
                EventKind = t.EventKind,
                State = NotificationsConstants.TemplateVersionStates.Draft,
                CreatedAt = now,
                UpdatedAt = now,
            };
            var version = new TemplateVersion
            {
                Id = Guid.NewGuid(),
                TemplateId = template.Id,
                VersionNo = 1,
                State = NotificationsConstants.TemplateVersionStates.Draft,
                BodyAr = t.BodyAr,
                BodyEn = t.BodyEn,
                SubjectAr = t.SubjectAr,
                SubjectEn = t.SubjectEn,
                PlaceholdersJson = "[]",
                ArEditorialReviewed = false, // T058 — pending human review.
                AuthorId = SeedAuthorId,
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.Templates.Add(template);
            _db.TemplateVersions.Add(version);
        }
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("NotificationsV1Seeder seeded {Count} templates (all ArEditorialReviewed=false).", Templates.Length);
    }
}
