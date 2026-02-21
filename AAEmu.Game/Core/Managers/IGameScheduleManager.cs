using AAEmu.Game.Models.Game.Schedules;

namespace AAEmu.Game.Core.Managers;

public interface IGameScheduleManager
{
    void Load();
    void LoadGameSchedules(Dictionary<int, GameSchedules> gameSchedules);
    void LoadGameScheduleSpawners(Dictionary<int, GameScheduleSpawners> gameScheduleSpawners);
    void LoadGameScheduleDoodads(Dictionary<int, GameScheduleDoodads> gameScheduleDoodads);
}
