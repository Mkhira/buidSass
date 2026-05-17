using BackendApi.Modules.Notifications.Domain;
using BackendApi.Modules.Notifications.Persistence;
using BackendApi.Modules.Notifications.Primitives;
using BackendApi.Modules.Notifications.UnsubscribeTokens;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Notifications.Features.Preferences;

/// <summary>
/// T038 — preference Get / Update / Unsubscribe. V-4 invariant: a row with
/// <c>category=transactional</c> MUST remain <c>enabled=true</c> — set-to-
/// false is rejected at the app layer (this handler) AND at the DB trigger.
/// </summary>

public sealed record GetPreferencesQuery(Guid CustomerId) : IRequest<IReadOnlyList<PreferenceView>>;

public sealed record PreferenceView(string Channel, string Category, bool Enabled, DateTimeOffset UpdatedAt);

public sealed record UpdatePreferenceCommand(
    Guid CustomerId,
    string Channel,
    string Category,
    bool Enabled) : IRequest<Unit>;

public sealed record UnsubscribeCommand(string SignedToken) : IRequest<bool>;

public sealed class GetPreferencesHandler : IRequestHandler<GetPreferencesQuery, IReadOnlyList<PreferenceView>>
{
    private readonly NotificationsDbContext _db;

    public GetPreferencesHandler(NotificationsDbContext db) { _db = db; }

    public async Task<IReadOnlyList<PreferenceView>> Handle(GetPreferencesQuery request, CancellationToken ct)
    {
        return await _db.Preferences.AsNoTracking()
            .Where(p => p.CustomerId == request.CustomerId)
            .OrderBy(p => p.Channel).ThenBy(p => p.Category)
            .Select(p => new PreferenceView(p.Channel, p.Category, p.Enabled, p.UpdatedAt))
            .ToListAsync(ct);
    }
}

public sealed class UpdatePreferenceHandler : IRequestHandler<UpdatePreferenceCommand, Unit>
{
    private readonly NotificationsDbContext _db;
    private readonly TimeProvider _clock;

    public UpdatePreferenceHandler(NotificationsDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Unit> Handle(UpdatePreferenceCommand request, CancellationToken ct)
    {
        if (request.Category == NotificationsConstants.Categories.Transactional && !request.Enabled)
            throw new InvalidOperationException("V-4: transactional preferences cannot be disabled.");

        var row = await _db.Preferences.FirstOrDefaultAsync(
            p => p.CustomerId == request.CustomerId
                && p.Channel == request.Channel
                && p.Category == request.Category, ct);

        if (row is null)
        {
            _db.Preferences.Add(new Preference
            {
                CustomerId = request.CustomerId,
                Channel = request.Channel,
                Category = request.Category,
                Enabled = request.Enabled,
                UpdatedAt = _clock.GetUtcNow(),
            });
        }
        else
        {
            row.Enabled = request.Enabled;
            row.UpdatedAt = _clock.GetUtcNow();
        }
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public sealed class UnsubscribeHandler : IRequestHandler<UnsubscribeCommand, bool>
{
    private readonly NotificationsDbContext _db;
    private readonly UnsubscribeTokenService _tokens;
    private readonly TimeProvider _clock;

    public UnsubscribeHandler(
        NotificationsDbContext db,
        UnsubscribeTokenService tokens,
        TimeProvider clock)
    {
        _db = db;
        _tokens = tokens;
        _clock = clock;
    }

    public async Task<bool> Handle(UnsubscribeCommand request, CancellationToken ct)
    {
        var token = await _tokens.ValidateAndConsumeAsync(request.SignedToken, ct);
        if (token is null) return false;

        var row = await _db.Preferences.FirstOrDefaultAsync(
            p => p.CustomerId == token.CustomerId
                && p.Channel == token.Channel
                && p.Category == token.Category, ct);

        if (row is null)
        {
            _db.Preferences.Add(new Preference
            {
                CustomerId = token.CustomerId,
                Channel = token.Channel,
                Category = token.Category,
                Enabled = false,
                UpdatedAt = _clock.GetUtcNow(),
            });
        }
        else
        {
            row.Enabled = false;
            row.UpdatedAt = _clock.GetUtcNow();
        }
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
