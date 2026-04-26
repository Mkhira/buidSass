using BackendApi.Modules.Returns.Primitives;

namespace BackendApi.Modules.Returns.Entities;

/// <summary>data-model.md table 5.</summary>
public sealed class Refund
{
    public Guid Id { get; set; }
    public Guid ReturnRequestId { get; set; }
    /// <summary>Per-market partitioning (Principle 5 / ADR-010).</summary>
    public string MarketCode { get; set; } = string.Empty;
    public string? ProviderId { get; set; }
    public string? CapturedTransactionId { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string State { get; set; } = RefundStateMachine.Pending;

    public DateTimeOffset InitiatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }

    public string? GatewayRef { get; set; }
    public string? FailureReason { get; set; }
    public int Attempts { get; set; }

    public string? ManualIban { get; set; }
    public string? ManualBeneficiaryName { get; set; }
    public string? ManualBankName { get; set; }
    public string? ManualReference { get; set; }
    public Guid? ManualConfirmedByAccountId { get; set; }
    public DateTimeOffset? ManualConfirmedAt { get; set; }

    public long RestockingFeeMinor { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public uint RowVersion { get; set; }

    public ReturnRequest? ReturnRequest { get; set; }
    public List<RefundLine> Lines { get; set; } = new();
}
