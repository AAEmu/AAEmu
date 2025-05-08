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
        }
        else
        {
            // TODO: Deal with channel dialog
            // For now just enter the main instance as channel 0
            IndunManager.Instance.RequestSystemInstance(character, ZoneId, 0, out _);
        }

    }
}
