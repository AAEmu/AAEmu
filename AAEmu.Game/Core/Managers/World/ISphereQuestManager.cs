using System.Collections.Generic;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Managers.World
{
    public interface ISphereQuestManager
    {
        void AddSphereQuestTrigger(SphereQuestTrigger trigger);
        List<SphereQuest> GetQuestSpheres(uint componentId);
        List<SphereQuestTrigger> GetSphereQuestTriggers();
        void Initialize();
        void Load();
        void RemoveSphereQuestTrigger(SphereQuestTrigger trigger);
    }
}