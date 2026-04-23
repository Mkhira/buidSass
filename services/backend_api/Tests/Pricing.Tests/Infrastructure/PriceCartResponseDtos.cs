using System.Text.Json.Serialization;

namespace Pricing.Tests.Infrastructure;

public sealed record PriceCartResponseDto(
    IReadOnlyList<PriceCartResponseLineDto> Lines,
    PriceCartTotalsDto Totals,
    string Currency,
    string ExplanationHash);

public sealed record PriceCartResponseLineDto(
    Guid ProductId, int Qty, long ListMinor, long NetMinor, long TaxMinor, long GrossMinor,
    IReadOnlyList<PriceCartLayerDto> Layers);

public sealed record PriceCartLayerDto(string Layer, string? RuleId, string? RuleKind, long AppliedMinor);

public sealed record PriceCartTotalsDto(long SubtotalMinor, long DiscountMinor, long TaxMinor, long GrandTotalMinor);

public sealed record ProblemDto([property: JsonPropertyName("reasonCode")] string ReasonCode);
