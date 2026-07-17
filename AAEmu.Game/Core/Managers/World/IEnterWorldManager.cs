using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Managers.World;

public interface IEnterWorldManager
{
    void AddAccount(ulong accountId, uint connectionId);
    void Login(GameConnection connection, ulong accountId, uint token);
    void Leave(GameConnection connection, LeaveWorldTargetType leaveWorldTargetType);
    void LeaveWorldTask(GameConnection connection, LeaveWorldTargetType leaveWorldTarget, Character activeChar);
}
