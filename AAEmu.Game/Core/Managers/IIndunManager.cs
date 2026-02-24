using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Indun;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Managers;

public interface IIndunManager : IInitializable
{
    bool InstanceHasChannels(uint zoneId);
    bool RequestSystemInstance(Character character, uint zoneId, uint channelId, out Dungeon dungeon);
    void DoIndunActions(uint startActionId, WorldInstance worldInstance);
    Dungeon CreateSystemInstance(Character character, uint zoneKey, uint channelId, bool overrideInstanceId = false, uint fixedInstanceId = 0);
}
