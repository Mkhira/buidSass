using BackendApi.Modules.Shipping.Domain;
using BackendApi.Modules.Shipping.Persistence;
using BackendApi.Modules.Shipping.Primitives;
using MediatR;

namespace BackendApi.Modules.Shipping.Features.Zones;

public sealed record CreateZoneCommand(
    string MarketCode,
    string NameAr,
    string NameEn,
    IReadOnlyList<string> PostalCodePrefixes,
    IReadOnlyList<string> CityList) : IRequest<CreateZoneResult>;

public sealed record CreateZoneResult(Guid ZoneId);

public sealed class CreateZoneHandler(ShippingDbContext db, TimeProvider clock)
    : IRequestHandler<CreateZoneCommand, CreateZoneResult>
{
    public async Task<CreateZoneResult> Handle(CreateZoneCommand cmd, CancellationToken ct)
    {
        if (!ShippingConstants.Markets.IsValid(cmd.MarketCode))
        {
            throw new ArgumentException("MarketCode must be SA or EG", nameof(cmd));
        }
        if (string.IsNullOrWhiteSpace(cmd.NameAr) || string.IsNullOrWhiteSpace(cmd.NameEn))
        {
            throw new ArgumentException("Both AR and EN names are required.", nameof(cmd));
        }
        var nowUtc = clock.GetUtcNow();
        var zone = new ShippingZone
        {
            Id = Guid.NewGuid(),
            MarketCode = cmd.MarketCode,
            NameAr = cmd.NameAr.Trim(),
            NameEn = cmd.NameEn.Trim(),
            PostalCodePrefixesJson = System.Text.Json.JsonSerializer.Serialize(cmd.PostalCodePrefixes),
            CityListJson = System.Text.Json.JsonSerializer.Serialize(cmd.CityList.Select(c => c.ToLowerInvariant())),
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
        db.ShippingZones.Add(zone);
        await db.SaveChangesAsync(ct);
        return new CreateZoneResult(zone.Id);
    }
}
