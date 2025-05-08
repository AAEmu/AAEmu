using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncRemoveInstance : DoodadFuncTemplate
{
    // doodad_funcs
    public uint ZoneId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Debug($"DoodadFuncRemoveInstance, ZoneId: {ZoneId}");
        var zone = ZoneManager.Instance.GetZoneById(ZoneId);
        if (caster is Character character && zone != null)
        {
            if (IndunManager.Instance.RequestDeletion(character, zone))
            {
                Logger.Info($"DoodadFuncRemoveInstance, ZoneId: {ZoneId} has been removed");
            }
        }
    }
}
