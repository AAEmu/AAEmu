using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Indun;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Managers;

public interface IIndunManager
{
    void Initialize();
    bool InstanceHasChannels(uint zoneId);
    bool RequestSystemInstance(Character character, uint zoneId, uint channelId, out Dungeon dungeon);
    void DoIndunActions(uint startActionId, WorldInstance worldInstance);
}
