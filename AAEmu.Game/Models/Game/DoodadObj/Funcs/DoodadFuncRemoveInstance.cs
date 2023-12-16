using AAEmu.Game.Core.Managers;
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
        Logger.Info("DoodadFuncRemoveInstance, ZoneId: {0}", ZoneId);
        if (caster is Character character)
        {
            var dungeon = IndunManager.Instance.GetSoloDungeon(character.Id);
            if (dungeon != null && IndunManager.Instance.RequestDeletion(character, dungeon))
            {
                Logger.Info("[Dungeon]" + ZoneId + ": " + " Destroying Solo Dungeon...");
            }
        }
    }
}
