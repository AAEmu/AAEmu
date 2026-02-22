using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Managers.World;

public interface IAreaTriggerManager : IInitializable
{
    void AddAreaTrigger(AreaTrigger trigger);
    void RemoveAreaTrigger(AreaTrigger trigger);
    void Tick(TimeSpan delta);
}
