using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Schedules;

namespace AAEmu.Game.Core.Managers;

public interface IGameScheduleManager : ILoadable
{
    void LoadGameSchedules(Dictionary<int, GameSchedules> gameSchedules);
    void LoadGameScheduleSpawners(Dictionary<int, GameScheduleSpawners> gameScheduleSpawners);
    void LoadGameScheduleDoodads(Dictionary<int, GameScheduleDoodads> gameScheduleDoodads);
    NpcSpawnerWindowState GetSpawnerWindowState(uint spawnerTemplateId);
    HashSet<uint> GetClosedSpawnerTemplateIds();
    HashSet<uint> GetScheduledSpawnerTemplateIds();
    HashSet<int> GetRunningGameScheduleIds();
    IReadOnlyList<uint> GetSpawnerIdsForSchedule(int gameScheduleId);
}
