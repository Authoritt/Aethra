using Aethra.Modules.Identity.Domain.Totp;
using Aethra.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Aethra.Modules.Identity.Infrastructure.Authentication;

/// <summary>
/// Estado scoped del challenge seleccionado por el JWT y operaciones condicionales que hacen
/// cumplir sus limites en la base compartida por todas las replicas.
/// </summary>
internal interface ITotpChallengePersistence
{
    void Create(TotpChallenge challenge);
    bool IsActive(string challengeId, string userId, DateTime utcNow);
    Task<bool> TryConsumeAsync(string challengeId, DateTime utcNow, CancellationToken ct);
    Task RegisterFailedAttemptAsync(string challengeId, DateTime utcNow, CancellationToken ct);
}

internal sealed class EfTotpChallengePersistence(IdentityDbContext db) : ITotpChallengePersistence
{
    public void Create(TotpChallenge challenge)
    {
        db.TotpChallenges.Add(challenge);
        db.SaveChanges();
    }

    public bool IsActive(string challengeId, string userId, DateTime utcNow)
        => db.TotpChallenges
            .AsNoTracking()
            .Any(c => c.Id == challengeId
                && c.UserId == userId
                && c.ConsumedAt == null
                && c.ExpiresAt > utcNow
                && c.FailedAttempts < TotpChallenge.MaxFailedAttempts);

    public async Task<bool> TryConsumeAsync(string challengeId, DateTime utcNow, CancellationToken ct)
    {
        var affected = await db.TotpChallenges
            .Where(c => c.Id == challengeId
                && c.ConsumedAt == null
                && c.ExpiresAt > utcNow
                && c.FailedAttempts < TotpChallenge.MaxFailedAttempts)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(c => c.ConsumedAt, utcNow), ct)
            .ConfigureAwait(false);
        return affected == 1;
    }

    public async Task RegisterFailedAttemptAsync(string challengeId, DateTime utcNow, CancellationToken ct)
        => await db.TotpChallenges
            .Where(c => c.Id == challengeId
                && c.ConsumedAt == null
                && c.ExpiresAt > utcNow
                && c.FailedAttempts < TotpChallenge.MaxFailedAttempts)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(c => c.FailedAttempts, c => c.FailedAttempts + 1), ct)
            .ConfigureAwait(false);
}

internal sealed class TotpChallengeStore(ITotpChallengePersistence persistence, IClock clock)
{
    private string? _currentChallengeId;

    public void Create(string challengeId, string userId)
    {
        var challenge = new TotpChallenge(challengeId, userId, clock.UtcNow);
        persistence.Create(challenge);
    }

    public bool TrySelectActive(string challengeId, string userId)
    {
        ClearCurrent();
        var now = clock.UtcNow.UtcDateTime;
        var active = persistence.IsActive(challengeId, userId, now);

        if (active)
        {
            _currentChallengeId = challengeId;
        }

        return active;
    }

    public void ClearCurrent() => _currentChallengeId = null;

    public async Task<bool> TryConsumeCurrentAsync(CancellationToken ct)
    {
        if (_currentChallengeId is null)
        {
            return false;
        }

        var challengeId = _currentChallengeId;
        var now = clock.UtcNow.UtcDateTime;
        var consumed = await persistence.TryConsumeAsync(challengeId, now, ct).ConfigureAwait(false);

        ClearCurrent();
        return consumed;
    }

    public async Task RegisterFailedAttemptAsync(CancellationToken ct)
    {
        if (_currentChallengeId is null)
        {
            return;
        }

        var challengeId = _currentChallengeId;
        var now = clock.UtcNow.UtcDateTime;
        await persistence.RegisterFailedAttemptAsync(challengeId, now, ct).ConfigureAwait(false);
        ClearCurrent();
    }
}
