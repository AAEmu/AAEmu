using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Managers;

public interface ITeamManager
{
    void Load();
    Team GetActiveTeamByUnit(uint unitId);
    Team GetTeamByObjId(uint objId);
    Team GetActiveTeam(uint teamId);
}
