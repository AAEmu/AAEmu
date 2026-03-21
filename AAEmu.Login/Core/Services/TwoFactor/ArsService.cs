using System.Collections.Concurrent;
using System.Security.Cryptography;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Services.TwoFactor;

public class ArsService(TimeProvider timeProvider, ILogger<ArsService> logger) : IArsService
{
    private readonly ConcurrentDictionary<AccountId, ArsSession> _activeSessions = new();

    public Task<ArsSession> CreateSessionAsync(AccountId accountId, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        // Generate a random 4-digit code
        var code = RandomNumberGenerator.GetInt32(0, 10000).ToString("D4");
        var expiresAt = timeProvider.GetUtcNow().AddSeconds(timeoutSeconds);

        var session = new ArsSession(code, expiresAt);

        // Store or replace any existing session for this account
        _activeSessions.AddOrUpdate(accountId, static (_, session) => session, static (_, _, session) => session,
            session);

        logger.LogDebug("Created ARS session for account {AccountId}, expires at {ExpiresAt}", accountId.Value,
            expiresAt);

        // Note: In a real implementation, this would call IArsPhoneProvider.InitiateCallAsync
        // to place an actual phone call. For now, this is a stub.

        return Task.FromResult(session);
    }

    public Task<bool> CompleteSessionAsync(AccountId accountId, string sessionCode, CancellationToken cancellationToken = default)
    {
        if (!_activeSessions.TryGetValue(accountId, out var session))
        {
            logger.LogDebug("ARS completion attempted for account {AccountId} with no active session", accountId.Value);
            return Task.FromResult(false);
        }

        // Check if session has expired
        var now = timeProvider.GetUtcNow();
        if (now > session.ExpiresAt)
        {
            _activeSessions.TryRemove(accountId, out _);
            logger.LogDebug("ARS session for account {AccountId} has expired", accountId.Value);
            return Task.FromResult(false);
        }

        // Verify the code matches
        if (!string.Equals(session.SessionCode, sessionCode, StringComparison.Ordinal))
        {
            logger.LogDebug("ARS code mismatch for account {AccountId}", accountId.Value);
            return Task.FromResult(false);
        }

        // Success - remove the session
        _activeSessions.TryRemove(accountId, out _);
        logger.LogDebug("ARS session completed successfully for account {AccountId}", accountId.Value);

        return Task.FromResult(true);
    }
}
