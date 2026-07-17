using AAEmu.Game.Core.Network.Connections;

namespace AAEmu.Game.Core.Managers.World;

public interface IStreamManager
{
    void AddToken(ulong accountId, uint connectionId);
    void RemoveToken(uint token);
    void Login(StreamConnection connection, ulong accountId, uint token);
}
