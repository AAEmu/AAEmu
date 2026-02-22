using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Managers;

public interface ITeamManager : ILoadable
{
    Team GetActiveTeamByUnit(uint unitId);
    Team GetTeamByObjId(uint objId);
    Team GetActiveTeam(uint teamId);
    void MemberRemoveFromTeam(Character unit, Character source, RiskyAction leaveType);
}
