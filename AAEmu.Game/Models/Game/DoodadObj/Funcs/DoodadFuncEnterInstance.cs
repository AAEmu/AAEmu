using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncEnterInstance : DoodadFuncTemplate
{
    // doodad_funcs
    public uint ZoneId { get; set; }
    public uint ItemId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Info("DoodadFuncEnterInstance, ZoneId: {0}, ItemId: {1}", ZoneId, ItemId);

        if (caster is Character character)
        {
            if (character.MainWorldPosition == null)
            {
                character.MainWorldPosition = character.Transform.CloneDetached(character); // сохраним координаты для возврата в основной мир
            }
            else if (character.Transform.WorldId == 0)
            {
                character.MainWorldPosition = character.Transform.CloneDetached(character); // сохраним координаты для возврата в основной мир
            }

            IndunManager.Instance.RequestInstance(character, ZoneId);
        }
    }
}
