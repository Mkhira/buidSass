namespace BackendApi.Modules.Pricing.Entities;

/// <summary>
/// Spec 007-b high-impact approval row. Data-model §2.7.
/// Unique constraint <c>(target_entity_kind, target_entity_id)</c> enforces "one approval per draft"
/// (R12 layer 2). Check constraint <c>approver_actor_id &lt;&gt; author_actor_id</c> enforces
/// separation-of-duties at the DB layer.
/// </summary>
public sealed class CommercialApproval
{
    public Guid Id { get; set; }
    public string TargetEntityKind { get; set; } = string.Empty; // coupon | promotion
    public Guid TargetEntityId { get; set; }
    public Guid AuthorActorId { get; set; }
    public Guid ApproverActorId { get; set; }
    public string CosignNote { get; set; } = string.Empty;
    public DateTimeOffset ApprovedAtUtc { get; set; }
    public uint XminRowVersion { get; set; }
}
