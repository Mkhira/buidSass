using BackendApi.Modules.Payments.Domain;
using BackendApi.Modules.Payments.Domain.StateMachines;
using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;
using BackendApi.Modules.Payments.Providers;
using BackendApi.Modules.Payments.Services;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Payments.Features.CreatePayment;

/// <summary>
/// T018 — creates a Payment row for an order with idempotent semantics (BR-3
/// / V-2). For BNPL the resolved Payment is in <c>pending_external_redirect</c>;
/// for COD it is <c>pending_collection_on_delivery</c>; for bank-transfer it
/// is <c>pending_bank_transfer</c> with a generated reference; for cards it
/// is <c>pending_authorization</c> for the dispatch worker to pick up.
/// </summary>
public sealed record CreatePaymentCommand(
    Guid OrderId,
    Guid CustomerId,
    string MarketCode,
    string Method,
    decimal Amount,
    string Currency,
    Guid AttemptId) : IRequest<CreatePaymentResponse>;

public sealed record CreatePaymentResponse(
    Guid PaymentId,
    string State,
    string? ProviderId,
    string? HostedFieldsConfigJson,
    string? ExternalRedirectUrl,
    string? BankTransferReference,
    bool IdempotentReplay);

public sealed class CreatePaymentValidator : AbstractValidator<CreatePaymentCommand>
{
    public CreatePaymentValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.MarketCode)
            .Must(PaymentsConstants.Markets.IsValid)
            .WithMessage("Market must be one of: sa, eg");
        RuleFor(x => x.Method)
            .Must(m => PaymentsConstants.Methods.All.Contains(m))
            .WithMessage("Unknown payment method");
        RuleFor(x => x.Currency)
            .Must((cmd, currency) => currency == PaymentsConstants.Currencies.For(cmd.MarketCode))
            .WithMessage("Currency must match market");
        RuleFor(x => x.Amount).GreaterThan(0m);
        RuleFor(x => x.AttemptId).NotEmpty();
        RuleFor(x => x)
            .Must(x => PaymentsConstants.Methods.EligibleForMarket(x.Method, x.MarketCode))
            .WithMessage("Method not eligible for market (V-7)");
    }
}

public sealed class CreatePaymentHandler : IRequestHandler<CreatePaymentCommand, CreatePaymentResponse>
{
    private readonly PaymentsDbContext _db;
    private readonly ProviderRegistry _providers;
    private readonly IValidator<CreatePaymentCommand> _validator;
    private readonly TimeProvider _clock;

    public CreatePaymentHandler(
        PaymentsDbContext db,
        ProviderRegistry providers,
        IValidator<CreatePaymentCommand> validator,
        TimeProvider clock)
    {
        _db = db;
        _providers = providers;
        _validator = validator;
        _clock = clock;
    }

    public async Task<CreatePaymentResponse> Handle(CreatePaymentCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var idempotencyKey = IdempotencyKeyDerivation.Derive(command.OrderId, command.Method, command.AttemptId);

        // V-2 — idempotent replay returns the original Payment without any side effect.
        var existing = await _db.Payments
            .FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey && p.DeletedAt == null, ct);
        if (existing is not null)
        {
            return await BuildReplayResponseAsync(existing, ct);
        }

        // Resolve the provider (nullable for COD / bank_transfer — native methods).
        string? providerId = null;
        if (command.Method != PaymentsConstants.Methods.Cod
            && command.Method != PaymentsConstants.Methods.BankTransfer)
        {
            var routing = await _db.ProviderRoutings.FirstOrDefaultAsync(
                r => r.MarketCode == command.MarketCode && r.Method == command.Method, ct);
            if (routing is null)
            {
                throw new InvalidOperationException(
                    $"No provider routing configured for ({command.MarketCode},{command.Method})");
            }
            providerId = routing.PrimaryProviderId;
            // Defensive: ensure the chosen provider supports this market+method pair.
            if (!_providers.TryResolve(providerId, out var provider) || provider is null
                || !provider.SupportsMarket(command.MarketCode)
                || !provider.SupportsMethod(command.Method))
            {
                throw new InvalidOperationException(
                    $"Provider '{providerId}' does not support ({command.MarketCode},{command.Method})");
            }
        }

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = command.OrderId,
            MarketCode = command.MarketCode,
            Method = command.Method,
            ProviderId = providerId,
            State = PaymentStateMachine.InitialStateForMethod(command.Method),
            Amount = command.Amount,
            Currency = command.Currency,
            IdempotencyKey = idempotencyKey,
            AttemptId = command.AttemptId,
            CustomerId = command.CustomerId,
            RecipientPayloadRedactedJson = "{}",
            CreatedAt = _clock.GetUtcNow(),
            UpdatedAt = _clock.GetUtcNow(),
        };

        _db.Payments.Add(payment);
        _db.IdempotencyKeys.Add(new IdempotencyKey
        {
            Key = idempotencyKey,
            PaymentId = payment.Id,
            CreatedAt = _clock.GetUtcNow(),
            ExpiresAt = _clock.GetUtcNow().AddHours(24),
        });

        string? bankTransferRef = null;
        if (command.Method == PaymentsConstants.Methods.BankTransfer)
        {
            bankTransferRef = BankTransferReferenceGenerator.Generate(command.MarketCode, payment.Id);
            _db.BankTransferReferences.Add(new BankTransferReference
            {
                PaymentId = payment.Id,
                Reference = bankTransferRef,
                CreatedAt = _clock.GetUtcNow(),
                UpdatedAt = _clock.GetUtcNow(),
            });
        }

        if (command.Method == PaymentsConstants.Methods.Cod)
        {
            _db.CodCollectionLogs.Add(new CodCollectionLog
            {
                PaymentId = payment.Id,
                Outcome = string.Empty,
                CreatedAt = _clock.GetUtcNow(),
                UpdatedAt = _clock.GetUtcNow(),
            });
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // V-2 race: another request with the same idempotency key won the
            // unique-index race. Detach our staged entities and replay the
            // original Payment instead of surfacing a unique-constraint error.
            _db.ChangeTracker.Clear();
            var winner = await _db.Payments
                .FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey && p.DeletedAt == null, ct);
            if (winner is not null)
            {
                return await BuildReplayResponseAsync(winner, ct);
            }
            throw;
        }

        // For BNPL we synchronously resolve the external redirect URL to return
        // it to the client; the actual capture awaits the provider webhook.
        string? redirectUrl = null;
        string? hostedFieldsConfig = null;
        if (payment.State == PaymentsConstants.PaymentStates.PendingExternalRedirect && providerId is not null
            && _providers.TryResolve(providerId, out var bnplProvider) && bnplProvider is not null)
        {
            var result = await bnplProvider.CreatePaymentAsync(new CreatePaymentDispatch(
                payment.Id, payment.OrderId, payment.MarketCode, payment.Method,
                payment.Amount, payment.Currency, idempotencyKey,
                CustomerRef: payment.CustomerId.ToString("N"),
                RecipientNameRedacted: "redacted",
                RecipientPhoneMaskedLast4: "0000"), ct);
            redirectUrl = result.ExternalRedirectUrl;
            payment.ProviderMessageId = result.ProviderMessageId;
            payment.UpdatedAt = _clock.GetUtcNow();
            await _db.SaveChangesAsync(ct);
        }
        else if (payment.State == PaymentsConstants.PaymentStates.PendingAuthorization && providerId is not null
            && _providers.TryResolve(providerId, out var cardProvider) && cardProvider is not null)
        {
            // Hosted-fields config is returned eagerly; the dispatch worker
            // will perform the authorize-and-capture once the customer submits.
            hostedFieldsConfig = BuildHostedFieldsConfigStub(providerId, payment);
        }

        return new CreatePaymentResponse(
            payment.Id, payment.State, providerId, hostedFieldsConfig, redirectUrl, bankTransferRef, IdempotentReplay: false);
    }

    private async Task<CreatePaymentResponse> BuildReplayResponseAsync(Payment existing, CancellationToken ct)
    {
        // Bank transfer carries a per-payment reference clients still need on
        // replay (the customer is staring at "transfer to ref ABC" UI). Look
        // it up from `bank_transfer_references` so the replay path is just as
        // useful as the original.
        string? bankTransferRef = null;
        if (existing.Method == PaymentsConstants.Methods.BankTransfer)
        {
            bankTransferRef = await _db.BankTransferReferences
                .Where(r => r.PaymentId == existing.Id && r.DeletedAt == null)
                .Select(r => r.Reference)
                .FirstOrDefaultAsync(ct);
        }

        // For synchronous-capture card flows we can reconstruct the hosted-
        // fields config deterministically (it's purely a function of provider
        // + payment id) so clients that lost their first response can still
        // mount the iframe.
        string? hostedFieldsConfig = null;
        if (existing.State == PaymentsConstants.PaymentStates.PendingAuthorization
            && existing.ProviderId is not null)
        {
            hostedFieldsConfig = BuildHostedFieldsConfigStub(existing.ProviderId, existing);
        }

        return new CreatePaymentResponse(
            existing.Id, existing.State, existing.ProviderId,
            HostedFieldsConfigJson: hostedFieldsConfig,
            ExternalRedirectUrl: null,
            BankTransferReference: bankTransferRef,
            IdempotentReplay: true);
    }

    private static string BuildHostedFieldsConfigStub(string providerId, Payment payment) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            provider = providerId,
            session_id = payment.Id.ToString("N"),
            public_key = $"pk_sandbox_{providerId}",
            method = payment.Method,
            currency = payment.Currency,
        });
}
