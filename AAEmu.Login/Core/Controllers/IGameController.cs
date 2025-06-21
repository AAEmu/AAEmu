using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Controllers;

public interface IGameController
{
    bool TryGetParentId(GameServerId gsId, out GameServerId id);
    void Load();
    void Add(GameServerId gsId, List<GameServerId> mirrorsId, InternalConnection connection);
    void Remove(GameServerId gsId);
    Task RequestWorldListAsync(LoginConnection connection);
    void SetLoad(GameServerId gsId, byte load);
    void RequestEnterWorld(LoginConnection connection, GameServerId gsId);
    void EnterWorld(LoginConnection connection, GameServerId gsId, byte result);
}
