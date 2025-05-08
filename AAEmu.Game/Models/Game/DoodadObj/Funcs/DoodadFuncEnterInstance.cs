using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
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
        Logger.Info($"DoodadFuncEnterInstance, ZoneId: {ZoneId}, ItemId: {ItemId}");

        if (caster is Character character)
        {
            if (character.MainWorldPosition == null)
            {
                character.MainWorldPosition = character.Transform.CloneDetached(character); // сохраним координаты для возврата в основной мир
            }
            else if (character.Transform.InstanceId == WorldManager.DefaultInstanceId)
            {
                character.MainWorldPosition = character.Transform.CloneDetached(character); // сохраним координаты для возврата в основной мир
            }

            IndunManager.Instance.RequestDungeonInstance(character, ZoneId, 0);
        }
    }
}
