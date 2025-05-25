using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Managers.World;

public interface IWorldManager
{
    WorldInstance MainWorld { get; set; }

    void CreateStaticInstances();

    WorldInstance CreateWorldInstance(WorldTemplate worldTemplate, uint channelId, bool overrideInstanceId = false, uint fixedInstanceId = 0, Character notifyPlayer = null);

    WorldTemplate CreateWorldTemplate(string worldName);
}
