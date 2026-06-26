using System.Net;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.PacketHandlers.C2L;
using AAEmu.Login.Core.Services;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Controllers;

public interface ILoginController
{
    void AddReconnectionToken(InternalConnection connection, GameServerId gsId, AccountId accountId, uint token);
    Task<ReconnectResult> Reconnect(GameServerId gsId, AccountId accountId, uint token);

    /// <summary>
    /// Eu Method Auth.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="password">The password sent by the client, with its encoding kind.</param>
    /// <param name="ip">The client IP address for recording.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<LoginResult> Login(string username, Password password, IPAddress ip, CancellationToken cancellationToken);

    /// <summary>
    /// Token-trusted authentication (web/launcher auth). Looks up the account by username and
    /// authenticates it WITHOUT verifying a password — the launcher session token is trusted upstream.
    /// Creates the account if it does not exist. Denies only when the username is invalid or banned.
    /// </summary>
    /// <param name="username">The account name (extracted from the launcher passport).</param>
    /// <param name="ip">The client IP address for recording.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<LoginResult> LoginTrusted(string username, IPAddress ip, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the Korea challenge-response authentication material for the given username.
    /// Used by <see cref="AAEmu.Login.Core.Authentication.KoreaAuthFlow"/> to seed the V2 challenge.
    /// </summary>
    /// <returns>
    /// The auth info if the account exists and has a <c>korea_challenge_hash</c> stored;
    /// <c>null</c> if the account does not exist or has no hash yet
    /// (user must log in via EU auth first to seed the hash when <c>KoreaChallengeAuth:Enabled=true</c>).
    /// </returns>
    Task<KoreaAuthInfo?> GetKoreaAuthInfoAsync(string username, CancellationToken cancellationToken);

    /// <summary>
    /// Checks the ban status of an account.
    /// Called after successful challenge verification to avoid leaking account status before auth.
    /// </summary>
    Task<(bool Banned, LoginDeniedReason BanReason)> CheckBanStatusAsync(
        AccountId accountId, CancellationToken cancellationToken);
}
