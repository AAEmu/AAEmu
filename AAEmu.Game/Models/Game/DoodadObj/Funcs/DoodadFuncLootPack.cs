using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncLootPack : DoodadFuncTemplate
{
    // doodad_funcs
    public uint LootPackId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        if (caster is not Character character)
            return;

        var actAbility = SkillManager.Instance.GetSkillActAbility(skillId);

        var lootPack = LootGameData.Instance.GetPack(LootPackId);
        if (lootPack == null)
        {
            Logger.Error("Doodad {0} requested missing loot pack {1}", owner.ObjId, LootPackId);
            return;
        }

        var lootPackContents = lootPack.GeneratePack(character, actAbility);

        // GiveLootPack performs the exact stack-aware capacity check. A raw free-slot
        // comparison incorrectly rejects rewards that fit into existing stacks.
        if (lootPack.GiveLootPack(character, actAbility, ItemTaskType.DoodadInteraction, lootPackContents))
        {
            owner.ToNextPhase = true;
            return;
        }

        character.SendErrorMessage(ErrorMessageType.BagFull);
    }
}
