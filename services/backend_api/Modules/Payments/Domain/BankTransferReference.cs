namespace BackendApi.Modules.Payments.Domain;

/// <summary>
/// Per-Payment unique reference for bank-transfer matching (BR-10).
/// Reference format: <c>&lt;MARKET&gt;-&lt;UUID4-FIRST-8&gt;-&lt;SHORT-HASH&gt;</c>
/// e.g., <c>SA-3a7f9c12-X8K2</c> — human-readable enough for bank-statement
/// memo fields (typically 16–32 chars).
/// </summary>
public sealed class BankTransferReference
{
    public Guid PaymentId { get; set; }
    public string Reference { get; set; } = string.Empty;

    /// <summary>jsonb snapshot of the matched bank-statement entry (populated on operator match).</summary>
    public string? MatchedBankStatementEntryJson { get; set; }

    public DateTimeOffset? MatchedAt { get; set; }
    public Guid? MatchedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}
