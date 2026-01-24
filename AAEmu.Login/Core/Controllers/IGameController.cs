using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Controllers;

public interface IGameController
{
    bool TryGetParentId(GameServerId gsId, out GameServerId id);
    void Load();
    void Add(GameServerId gsId, List<GameServerId> mirrorsId, InternalConnection connection);
    void Remove(GameServerId gsId);

    /// <summary>
    /// Requests the list of available game worlds from the specified login connection.
    /// </summary>
    /// <param name="connection">The client connection making the request.</param>
    Task RequestWorldListAsync(LoginConnection connection);

    void SetLoad(GameServerId gsId, byte load);
    void RequestEnterWorld(ILoginConnection connection, GameServerId gsId);
    Task EnterWorldAsync(ILoginConnection connection, GameServerId gsId, byte result);
}
