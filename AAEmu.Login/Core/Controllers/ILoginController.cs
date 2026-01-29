using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Controllers;

public interface ILoginController
{
    void AddReconnectionToken(InternalConnection connection, GameServerId gsId, AccountId accountId, uint token);
    Task Reconnect(ILoginConnection connection, GameServerId gsId, AccountId accountId, uint token);

    /// <summary>
    /// Kr Method Auth
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="username"></param>
    Task Login(ILoginConnection connection, string username);

    /// <summary>
    /// Eu Method Auth
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="username"></param>
    /// <param name="password"></param>
    Task Login(ILoginConnection connection, string username, ReadOnlyMemory<byte> password);
}
