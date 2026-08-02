using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Managers.World;

public interface IEnterWorldManager
{
    void AddAccount(uint accountId, uint connectionId);
    void Login(GameConnection connection, uint accountId, uint token);
    void Leave(GameConnection connection, LeaveWorldTargetType leaveWorldTargetType);
    bool ReturnToCharacterSelect(GameConnection connection, string reason);
    void LeaveWorldTask(GameConnection connection, LeaveWorldTargetType leaveWorldTarget, Character activeChar);
}
