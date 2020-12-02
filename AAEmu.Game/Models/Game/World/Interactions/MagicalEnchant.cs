using System.Collections.Generic;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.World.Interactions
{
    public class MagicalEnchant : IWorldInteraction
    {
        public void Execute(Unit caster, SkillCaster casterType, BaseUnit target, SkillCastTarget targetType,
            uint skillId, uint itemId, DoodadFuncTemplate objectFunc)
        {
            if (!(caster is Character character))
                return;

            if (!(targetType is SkillCastItemTarget itemTarget))
                return;

            if (!(casterType is SkillItem skillItem))
                return;

            var targetItem = character.Inventory.Bag.GetItemByItemId(itemTarget.Id);

            if (!(targetItem is EquipItem equipItem))
                return;

            equipItem.RuneId = skillItem.ItemTemplateId;
            
            character.SendPacket(new SCItemSocketingLunastoneResultPacket(true, targetItem.Id, skillItem.ItemTemplateId));
            character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.EnchantMagical, new List<ItemTask>() { new ItemUpdate(equipItem) }, new List<ulong>()));
        }
    }
}
