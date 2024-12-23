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
        var lootPackContents = lootPack.GeneratePack(character, actAbility);

        if (character.Inventory.Bag.FreeSlotCount >= lootPackContents.Count)
        {
            lootPack.GiveLootPack(character, actAbility, ItemTaskType.DoodadInteraction, lootPackContents);
            owner.ToNextPhase = true;

            return;
        }
        // TODO: make sure the doodad is marked as loot-able when not enough inventory space

        character.SendErrorMessage(ErrorMessageType.BagFull);
    }
}
