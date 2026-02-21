using AAEmu.Game.Core.Network.Connections;

namespace AAEmu.Game.Core.Managers.World;

public interface IEnterWorldManager
{
    void AddAccount(uint accountId, uint connectionId);
    void Login(GameConnection connection, uint accountId, uint token);
}
