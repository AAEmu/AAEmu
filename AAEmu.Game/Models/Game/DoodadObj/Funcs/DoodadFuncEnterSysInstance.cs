using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncEnterSysInstance : DoodadFuncTemplate
{
    // doodad_funcs
    public uint ZoneId { get; set; }
    public FactionsEnum FactionId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Info($"DoodadFuncEnterSysInstance, ZoneId: {ZoneId}, FactionId: {FactionId}");
        if (caster is not Character character)
        {
            return;
        }

        // Set main world when requesting to exit the main world or if it was never set before
        if (character.MainWorldPosition == null || character.Transform.InstanceId == WorldManager.DefaultInstanceId)
        {
            character.MainWorldPosition = character.Transform.CloneDetached(character); // сохраним координаты для возврата в основной мир
        }

        if (!IndunManager.Instance.InstanceHasChannels(ZoneId))
        {
            // Enter with channel 0 if no channel support
            IndunManager.Instance.RequestSystemInstance(character, ZoneId, 0, out _);
            return;
        }

        // Instances that offer channels open the client's picker on this list instead of entering: the
        // player's choice comes back as the copy to enter, which the entry then follows.
        if (IndunManager.Instance.SendChannelList(character, ZoneId))
        {
            return;
        }

        // No copy of the instance is up, so there is no dimension to offer and the list would leave the player
        // with a door that does nothing. Doors on instances that do not offer channels enter channel 0, and
        // these must keep a way in too — the instances whose copies are hosted only on demand are the normal
        // case, not an error.
        Logger.Info(
            "DoodadFuncEnterSysInstance: no hosted copy of zone {0} to offer {1}, entering channel 0",
            ZoneId, character.Name);
        IndunManager.Instance.RequestSystemInstance(character, ZoneId, 0, out _);
    }
}
