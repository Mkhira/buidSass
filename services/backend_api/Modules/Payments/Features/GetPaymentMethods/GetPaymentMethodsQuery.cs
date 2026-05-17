using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Payments.Features.GetPaymentMethods;

/// <summary>
/// T019 — returns the enabled payment methods for the given market filtered
/// by cart-total eligibility (BR-9) and V-7 market eligibility. Customer
/// endpoint backing <c>GET /payments/methods</c>.
/// </summary>
public sealed record GetPaymentMethodsQuery(string MarketCode, decimal CartTotal)
    : IRequest<IReadOnlyList<PaymentMethodOption>>;

public sealed record PaymentMethodOption(string Method, string Currency, decimal? MinCartTotal, decimal? MaxCartTotal);

public sealed class GetPaymentMethodsHandler : IRequestHandler<GetPaymentMethodsQuery, IReadOnlyList<PaymentMethodOption>>
{
    private readonly PaymentsDbContext _db;

    public GetPaymentMethodsHandler(PaymentsDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<PaymentMethodOption>> Handle(GetPaymentMethodsQuery query, CancellationToken ct)
    {
        if (!PaymentsConstants.Markets.IsValid(query.MarketCode))
        {
            return Array.Empty<PaymentMethodOption>();
        }
        var currency = PaymentsConstants.Currencies.For(query.MarketCode);
        var configs = await _db.PaymentMethodsMarketConfigs
            .Where(c => c.MarketCode == query.MarketCode && c.Enabled)
            .ToListAsync(ct);

        var result = new List<PaymentMethodOption>();
        foreach (var c in configs)
        {
            // V-7 market eligibility — defensive double-check.
            if (!PaymentsConstants.Methods.EligibleForMarket(c.Method, query.MarketCode)) continue;
            if (c.MinCartTotal is decimal min && query.CartTotal < min) continue;
            if (c.MaxCartTotal is decimal max && query.CartTotal > max) continue;
            result.Add(new PaymentMethodOption(c.Method, currency, c.MinCartTotal, c.MaxCartTotal));
        }
        return result;
    }
}
