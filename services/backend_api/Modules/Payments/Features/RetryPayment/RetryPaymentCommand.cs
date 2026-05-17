using BackendApi.Modules.Payments.Features.CreatePayment;
using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Payments.Features.RetryPayment;

/// <summary>
/// T039 — BR-6 retry: creates a NEW Payment row for the same order; never
/// mutates the failed row. The latest attempt wins for the order's
/// <c>payment_status</c> resolution.
/// </summary>
public sealed record RetryPaymentCommand(
    Guid OriginalPaymentId,
    Guid AttemptId,
    string? MethodOverride) : IRequest<CreatePaymentResponse>;

public sealed class RetryPaymentHandler : IRequestHandler<RetryPaymentCommand, CreatePaymentResponse>
{
    private readonly PaymentsDbContext _db;
    private readonly IMediator _mediator;

    public RetryPaymentHandler(PaymentsDbContext db, IMediator mediator)
    {
        _db = db;
        _mediator = mediator;
    }

    public async Task<CreatePaymentResponse> Handle(RetryPaymentCommand command, CancellationToken ct)
    {
        var original = await _db.Payments
            .FirstOrDefaultAsync(p => p.Id == command.OriginalPaymentId && p.DeletedAt == null, ct);
        if (original is null) throw new InvalidOperationException("Original payment not found");
        if (!PaymentsConstants.PaymentStates.Terminal.Contains(original.State))
        {
            throw new InvalidOperationException("Can only retry from a terminal state");
        }

        var method = command.MethodOverride ?? original.Method;
        // BR-17 soft rate limit — 5 attempts per customer per 24h on cards.
        if (IsCardLike(method))
        {
            var since = DateTimeOffset.UtcNow.AddHours(-24);
            var recent = await _db.Payments.CountAsync(p =>
                p.CustomerId == original.CustomerId
                && p.CreatedAt >= since
                && PaymentsConstants.PaymentStates.Terminal.Contains(p.State)
                && p.DeletedAt == null, ct);
            if (recent >= PaymentsConstants.Windows.CardSoftRateLimitPerCustomerPer24h)
            {
                throw new InvalidOperationException("Card retry soft rate-limit reached (BR-17)");
            }
        }

        var cmd = new CreatePaymentCommand(
            OrderId: original.OrderId,
            CustomerId: original.CustomerId,
            MarketCode: original.MarketCode,
            Method: method,
            Amount: original.Amount,
            Currency: original.Currency,
            AttemptId: command.AttemptId);
        return await _mediator.Send(cmd, ct);
    }

    private static bool IsCardLike(string method) =>
        method is PaymentsConstants.Methods.Card
            or PaymentsConstants.Methods.Mada
            or PaymentsConstants.Methods.Meeza
            or PaymentsConstants.Methods.ApplePay
            or PaymentsConstants.Methods.StcPay;
}
