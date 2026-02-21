using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Managers;

public interface IRadarManager
{
    void Initialize();
    void RegisterForPublicTransport(Character player, float checkRange);
    void RegisterForFishSchool(Character player, float checkRange);
    void RegisterForShips(Character player, float checkRange);
    void RadarTick(TimeSpan delta);
    void UnRegister(Character player);
}
