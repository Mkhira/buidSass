using BackendApi.Modules.Payments.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Payments.Features.GetPayment;

/// <summary>
/// Customer-facing payment lookup; deliberately excludes provider_message_id
/// (privacy) per spec §user-roles. Returns null when the payment does not
/// exist or is not owned by the customer.
/// </summary>
public sealed record GetPaymentQuery(Guid PaymentId, Guid CustomerId) : IRequest<PaymentDto?>;

public sealed record PaymentDto(
    Guid Id, Guid OrderId, string MarketCode, string Method, string State, decimal Amount, string Currency,
    DateTime CreatedAt, DateTime? CapturedAt, DateTime? FailedAt);

public sealed class GetPaymentHandler : IRequestHandler<GetPaymentQuery, PaymentDto?>
{
    private readonly PaymentsDbContext _db;
    public GetPaymentHandler(PaymentsDbContext db) { _db = db; }

    public async Task<PaymentDto?> Handle(GetPaymentQuery q, CancellationToken ct)
    {
        return await _db.Payments
            .Where(p => p.Id == q.PaymentId && p.CustomerId == q.CustomerId && p.DeletedAt == null)
            .Select(p => new PaymentDto(
                p.Id, p.OrderId, p.MarketCode, p.Method, p.State, p.Amount, p.Currency,
                p.CreatedAt.UtcDateTime,
                p.CapturedAt.HasValue ? p.CapturedAt.Value.UtcDateTime : null,
                p.FailedAt.HasValue ? p.FailedAt.Value.UtcDateTime : null))
            .FirstOrDefaultAsync(ct);
    }
}

public sealed record GetPaymentHistoryQuery(Guid CustomerId, int Take = 50)
    : IRequest<IReadOnlyList<PaymentDto>>;

public sealed class GetPaymentHistoryHandler : IRequestHandler<GetPaymentHistoryQuery, IReadOnlyList<PaymentDto>>
{
    private readonly PaymentsDbContext _db;
    public GetPaymentHistoryHandler(PaymentsDbContext db) { _db = db; }

    public async Task<IReadOnlyList<PaymentDto>> Handle(GetPaymentHistoryQuery q, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-90); // contract §1 — "Last 90 days"
        return await _db.Payments
            .Where(p => p.CustomerId == q.CustomerId && p.DeletedAt == null && p.CreatedAt >= since)
            .OrderByDescending(p => p.CreatedAt)
            .Take(Math.Clamp(q.Take, 1, 200))
            .Select(p => new PaymentDto(
                p.Id, p.OrderId, p.MarketCode, p.Method, p.State, p.Amount, p.Currency,
                p.CreatedAt.UtcDateTime,
                p.CapturedAt.HasValue ? p.CapturedAt.Value.UtcDateTime : null,
                p.FailedAt.HasValue ? p.FailedAt.Value.UtcDateTime : null))
            .ToListAsync(ct);
    }
}
