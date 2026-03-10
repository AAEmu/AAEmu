using System.Net;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Controllers;

public interface ILoginController
{
    void AddReconnectionToken(InternalConnection connection, GameServerId gsId, AccountId accountId, uint token);
    Task<ReconnectResult> Reconnect(GameServerId gsId, AccountId accountId, uint token);

    /// <summary>
    /// Kr Method Auth
    /// </summary>
    Task<LoginResult> Login(string username);

    /// <summary>
    /// Eu Method Auth
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="password">The password sent by the client.</param>
    /// <param name="ip">The client IP address for recording.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<LoginResult> Login(string username, string password, IPAddress ip, CancellationToken cancellationToken);
}
