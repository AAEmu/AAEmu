using System.Collections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Managers.World;

public interface IWorldManager : ILoadable
{
    WorldInstance MainWorld { get; set; }

    void CreateStaticInstances();

    WorldInstance CreateWorldInstance(WorldTemplate worldTemplate, uint channelId, bool overrideInstanceId = false, uint fixedInstanceId = 0, Character notifyPlayer = null);

    WorldTemplate CreateWorldTemplate(string worldName);

    Character GetCharacterByObjId(uint id);
    Character GetCharacterById(uint id);
    Character GetCharacter(string name);
    List<Character> GetAllCharacters();

    uint GetZoneId(WorldTemplate worldTemplate, float x, float y);
    WorldTemplate GetWorldTemplateByName(string worldName);
    WorldTemplate GetWorldTemplateByZoneKey(uint zoneKey);
    WorldInstance[] GetWorlds();
    WorldInstance GetWorld(uint worldInstanceId);
    List<uint> GetZoneKeysByWorldId(uint worldId);

    void BroadcastPacketToServer(GamePacket packet);
    Character GetTargetOrSelf(Character character, string targetName, out int firstNonNameArgument);
    bool TryRemoveCharacter(uint playerObjId);
    void Initialize();
    WorldTemplate[] GetAllWorldTemplates();
}
