using BackendApi.Modules.Returns.Primitives;

namespace BackendApi.Modules.Returns.Entities;

/// <summary>data-model.md table 3.</summary>
public sealed class Inspection
{
    public Guid Id { get; set; }
    public Guid ReturnRequestId { get; set; }
    public Guid InspectorAccountId { get; set; }
    public string State { get; set; } = InspectionStateMachine.Pending;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public ReturnRequest? ReturnRequest { get; set; }
    public List<InspectionLine> Lines { get; set; } = new();
}
