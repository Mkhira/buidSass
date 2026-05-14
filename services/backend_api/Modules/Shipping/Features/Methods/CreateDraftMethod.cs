using BackendApi.Modules.Shipping.Domain;
using BackendApi.Modules.Shipping.Persistence;
using BackendApi.Modules.Shipping.Primitives;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Shipping.Features.Methods;

public sealed record CreateDraftMethodCommand(
    string MarketCode,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    int EtaMinHours,
    int EtaMaxHours,
    Guid AuthorId,
    string EligibilityJson = "{}") : IRequest<CreateDraftMethodResult>;

public sealed record CreateDraftMethodResult(Guid MethodId, Guid VersionId, int VersionNo);

public sealed class CreateDraftMethodHandler(ShippingDbContext db, TimeProvider clock)
    : IRequestHandler<CreateDraftMethodCommand, CreateDraftMethodResult>
{
    public async Task<CreateDraftMethodResult> Handle(CreateDraftMethodCommand cmd, CancellationToken ct)
    {
        if (!ShippingConstants.Markets.IsValid(cmd.MarketCode))
        {
            throw new ArgumentException("MarketCode must be SA or EG", nameof(cmd));
        }
        if (string.IsNullOrWhiteSpace(cmd.NameAr) || string.IsNullOrWhiteSpace(cmd.NameEn))
        {
            // V-1 publish gate also enforced at publish; reject the draft here
            // so reviewers never see an incomplete method (Principle 4).
            throw new ArgumentException("Both AR and EN names are required (Principle 4)", nameof(cmd));
        }

        var nowUtc = clock.GetUtcNow();
        var method = new ShippingMethod
        {
            Id = Guid.NewGuid(),
            MarketCode = cmd.MarketCode,
            NameAr = cmd.NameAr.Trim(),
            NameEn = cmd.NameEn.Trim(),
            DescriptionAr = cmd.DescriptionAr,
            DescriptionEn = cmd.DescriptionEn,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
        var version = new ShippingMethodVersion
        {
            Id = Guid.NewGuid(),
            MethodId = method.Id,
            VersionNo = 1,
            State = MethodVersionStates.Draft,
            EligibilityJson = string.IsNullOrWhiteSpace(cmd.EligibilityJson) ? "{}" : cmd.EligibilityJson,
            EtaMinHours = cmd.EtaMinHours,
            EtaMaxHours = cmd.EtaMaxHours,
            AuthorId = cmd.AuthorId,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
        db.ShippingMethods.Add(method);
        db.ShippingMethodVersions.Add(version);
        await db.SaveChangesAsync(ct);
        return new CreateDraftMethodResult(method.Id, version.Id, version.VersionNo);
    }
}
