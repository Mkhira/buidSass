using BackendApi.Modules.Returns.Primitives;

namespace BackendApi.Modules.Returns.Entities;

/// <summary>data-model.md table 1.</summary>
public sealed class ReturnRequest
{
    public Guid Id { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public Guid AccountId { get; set; }
    public string MarketCode { get; set; } = string.Empty;
    public string State { get; set; } = ReturnStateMachine.PendingReview;

    public DateTimeOffset SubmittedAt { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string? CustomerNotes { get; set; }
    public string? AdminNotes { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }
    public Guid? DecidedByAccountId { get; set; }

    public bool ForceRefund { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public uint RowVersion { get; set; }

    public List<ReturnLine> Lines { get; set; } = new();
    public List<ReturnPhoto> Photos { get; set; } = new();
    public List<Inspection> Inspections { get; set; } = new();
    public List<Refund> Refunds { get; set; } = new();
    public List<ReturnStateTransition> Transitions { get; set; } = new();
}
