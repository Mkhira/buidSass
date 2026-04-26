using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Returns.Admin.Common;
using BackendApi.Modules.Returns.Common;
using BackendApi.Modules.Returns.Entities;
using BackendApi.Modules.Returns.Persistence;
using BackendApi.Modules.Returns.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Returns.Admin.Refunds.ConfirmBankTransfer;

public sealed record ConfirmBankTransferRequest(
    string Iban,
    string BeneficiaryName,
    string? BankName,
    string? Reference);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminConfirmBankTransferEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{refundId:guid}/confirm-bank-transfer", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("returns.refund.write");
        return builder;
    }

    /// <summary>FR-011 / FR-023. Manual COD bank-transfer confirmation. Advances the refund
    /// from <c>pending_manual_transfer</c> → <c>completed</c> and the return from any state
    /// reachable via the standard machine to <c>refunded</c>.</summary>
    private static async Task<IResult> HandleAsync(
        Guid refundId,
        ConfirmBankTransferRequest body,
        HttpContext context,
        ReturnsDbContext db,
        IAuditEventPublisher auditPublisher,
        CancellationToken ct)
    {
        var actorId = ReturnsResponseFactory.ResolveAccountId(context);
        if (actorId is null)
        {
            return ReturnsResponseFactory.Problem(context, 401, "returns.requires_auth", "Auth required");
        }
        if (body is null || string.IsNullOrWhiteSpace(body.Iban) || string.IsNullOrWhiteSpace(body.BeneficiaryName))
        {
            return ReturnsResponseFactory.Problem(context, 400, "refund.manual_iban.required",
                "iban and beneficiaryName are required.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // CR Critical round 5: lock both the refund row AND the parent return row before
        // validating fromState. Concurrent /retry from the worker + manual confirm could
        // otherwise race on the same refund (different state machines but shared parent).
        var refundLockExists = await db.Refunds
            .FromSqlInterpolated($"SELECT * FROM returns.refunds WHERE \"Id\" = {refundId} FOR UPDATE")
            .AsNoTracking()
            .AnyAsync(ct);
        if (!refundLockExists)
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 404, "return.not_found", "Refund not found.");
        }
        var refund = await db.Refunds.Include(rf => rf.Lines).FirstOrDefaultAsync(rf => rf.Id == refundId, ct);
        if (refund is null)
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 404, "return.not_found", "Refund not found.");
        }
        if (string.Equals(refund.State, RefundStateMachine.Completed, StringComparison.OrdinalIgnoreCase))
        {
            await tx.RollbackAsync(ct);
            return Results.Ok(new { id = refund.Id, state = refund.State, deduped = true });
        }
        if (!string.Equals(refund.State, RefundStateMachine.PendingManualTransfer, StringComparison.OrdinalIgnoreCase))
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 409, "return.state.illegal_transition",
                $"confirm-bank-transfer only valid from pending_manual_transfer (current: {refund.State}).");
        }

        var r = await db.ReturnRequests.FirstOrDefaultAsync(x => x.Id == refund.ReturnRequestId, ct);
        if (r is null)
        {
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 404, "return.not_found", "Return not found.");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var fromRefundState = refund.State;
        refund.State = RefundStateMachine.Completed;
        refund.ManualIban = body.Iban;
        refund.ManualBeneficiaryName = body.BeneficiaryName;
        refund.ManualBankName = body.BankName;
        refund.ManualReference = body.Reference;
        refund.ManualConfirmedByAccountId = actorId;
        refund.ManualConfirmedAt = nowUtc;
        refund.CompletedAt = nowUtc;
        refund.UpdatedAt = nowUtc;

        var fromReturnState = r.State;
        if (!AdminMutation.ValidateTransition(fromReturnState, ReturnStateMachine.Refunded))
        {
            // CR Major round 3: fail FAST — completing a refund while leaving the return
            // unchanged would desync the two state machines (the refund row says completed
            // but the return is still inspected/etc., yet the manual transfer has been
            // dispatched in the real world). Refuse to write so admin can investigate.
            await tx.RollbackAsync(ct);
            return ReturnsResponseFactory.Problem(context, 409, "return.state.illegal_transition",
                $"Return state {fromReturnState} cannot transition to refunded; investigate before re-confirming.");
        }
        r.State = ReturnStateMachine.Refunded;
        r.UpdatedAt = nowUtc;
        db.StateTransitions.Add(AdminMutation.NewReturnTransition(
            r.Id, r.MarketCode, fromReturnState, r.State, actorId.Value, "admin.confirm_bank_transfer",
            $"refundId={refund.Id}",
            new { refundId = refund.Id }, nowUtc));
        db.StateTransitions.Add(new ReturnStateTransition
        {
            ReturnRequestId = r.Id,
            MarketCode = r.MarketCode,
            RefundId = refund.Id,
            Machine = ReturnStateTransition.MachineRefund,
            FromState = fromRefundState,
            ToState = RefundStateMachine.Completed,
            ActorAccountId = actorId.Value,
            Trigger = "admin.confirm_bank_transfer",
            OccurredAt = nowUtc,
        });
        db.Outbox.Add(AdminMutation.NewOutbox("refund.manual_confirmed", r.Id, r.MarketCode, new
        {
            returnRequestId = r.Id,
            returnNumber = r.ReturnNumber,
            orderId = r.OrderId,
            refundId = refund.Id,
            amountMinor = refund.AmountMinor,
            currency = refund.Currency,
            // Explicit camelCase — dispatcher reads case-sensitively.
            lines = refund.Lines.Select(l => new { returnLineId = l.ReturnLineId, qty = l.Qty }),
        }, nowUtc));

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (AdminMutation.IsUniqueDedupViolation(ex))
        {
            await tx.RollbackAsync(ct);
            return Results.Ok(new { id = refund.Id, state = refund.State, deduped = true });
        }

        await AdminMutation.PublishAuditAsync(auditPublisher, actorId.Value, "returns.confirm_bank_transfer",
            r.Id, new { refundId = refund.Id }, new
            {
                refundId = refund.Id,
                iban = MaskIban(body.Iban),
                beneficiary = body.BeneficiaryName,
            }, body.Reference, ct);

        return Results.Ok(new
        {
            id = refund.Id,
            state = refund.State,
            returnState = r.State,
        });
    }

    private static string MaskIban(string iban)
    {
        var s = iban.Replace(" ", "");
        return s.Length <= 6 ? new string('*', s.Length) : s[..2] + new string('*', s.Length - 6) + s[^4..];
    }
}
