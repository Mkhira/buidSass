using BackendApi.Modules.Verification.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Verification.Customer.GetMarketSchema;

/// <summary>
/// Spec 020 task T100. Returns the active schema for the customer's market so
/// the customer app can render the verification form dynamically without
/// hardcoding profession options or regulator-id patterns. Returns
/// <c>null</c> when no schema is configured for the market.
/// </summary>
public sealed class GetMarketSchemaHandler(VerificationDbContext db)
{
    public async Task<GetMarketSchemaResponse?> HandleAsync(string marketCode, CancellationToken ct)
    {
        var schema = await db.MarketSchemas
            .AsNoTracking()
            .Where(s => s.MarketCode == marketCode && s.EffectiveTo == null)
            .OrderByDescending(s => s.Version)
            .FirstOrDefaultAsync(ct);

        if (schema is null)
        {
            return null;
        }

        return new GetMarketSchemaResponse(
            MarketCode: schema.MarketCode,
            Version: schema.Version,
            EffectiveFrom: schema.EffectiveFrom,
            RequiredFieldsJson: schema.RequiredFieldsJson,
            AllowedDocumentTypesJson: schema.AllowedDocumentTypesJson,
            ExpiryDays: schema.ExpiryDays,
            CooldownDays: schema.CooldownDays);
    }
}
