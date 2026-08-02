using AAEmu.Game.Core.Network.Connections;

namespace AAEmu.Game.Core.Managers.World;

public interface IStreamManager
{
    void AddToken(uint accountId, uint connectionId);
    void RemoveToken(uint token);
    void Login(StreamConnection connection, long accountId, uint token);
}
