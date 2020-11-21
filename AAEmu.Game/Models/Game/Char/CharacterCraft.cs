using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterCraft
    {
        private int _count { get; set; }
        private Craft _craft { get; set; }
        private uint _doodadId { get; set; }

        public Character Owner { get; set; }
        public bool IsCrafting = false;

        public CharacterCraft(Character owner) => Owner = owner;

        public void Craft(Craft craft, int count, uint doodadId)
        {
            _craft = craft;
            _count = count;
            _doodadId = doodadId;

            var hasMaterials = true;

            foreach (var craftMaterial in craft.CraftMaterials)
            {
                if (Owner.Inventory.GetItemsCount(craftMaterial.ItemId) < craftMaterial.Amount)
                    hasMaterials = false;
                /*
                var materialItem = Owner.Inventory.GetItemByTemplateId(craftMaterial.ItemId);
                if (materialItem == null || materialItem.Count < craftMaterial.Amount)
                {
                    hasMaterials = false;
                }
                */
            }

            if (_craft.IsPack)
            {
                var item = Owner.Inventory.GetEquippedBySlot(EquipmentItemSlot.Backpack);
                var backpackTemplate = (BackpackTemplate)item?.Template;
                if (backpackTemplate != null && backpackTemplate.BackpackType != BackpackType.Glider)
                {
                    // mb check to drop glider to inventory
                    //if (!Owner.Inventory.TakeoffBackpack())
                    CancelCraft();
                    return;
                }
            }

            if (hasMaterials)
            {
                IsCrafting = true;

                var caster = SkillCaster.GetByType(SkillCasterType.Unit);
                caster.ObjId = Owner.ObjId;

                var target = SkillCastTarget.GetByType(SkillCastTargetType.Doodad);
                target.ObjId = doodadId;

                var skill = new Skill(SkillManager.Instance.GetSkillTemplate(craft.SkillId));
                skill.Use(Owner, caster, target);
            }
        }

        public void EndCraft()
        {
            _count--;
            IsCrafting = false;

            if (_craft == null)
                return;

            if (Owner.Inventory.FreeSlotCount(SlotType.Inventory) < _craft.CraftProducts.Count)
                return;

            foreach (var material in _craft.CraftMaterials)
            {
                Owner.Inventory.Bag.ConsumeItem(Items.Actions.ItemTaskType.CraftActSaved, material.ItemId, material.Amount,null);
            }

            foreach (var product in _craft.CraftProducts)
            {
                // Check if we're crafting a tradepack, if so, try to remove currently equipped backpack slot
                if (ItemManager.Instance.IsAutoEquipTradePack(product.ItemId) == false)
                {
                    Owner.Inventory.Bag.AcquireDefaultItem(Items.Actions.ItemTaskType.CraftPickupProduct, product.ItemId, product.Amount);
                }
                else
                {
                    if (!Owner.Inventory.TryEquipNewBackPack(Items.Actions.ItemTaskType.CraftPickupProduct, product.ItemId, product.Amount))
                    {
                        CancelCraft();
                        return;
                    }
                    /*
                    // Remove player backpack
                    if (Owner.Inventory.TakeoffBackpack(Items.Actions.ItemTaskType.CraftPickupProduct,true))
                    {
                        // Put tradepack in their backpack slot
                        Owner.Inventory.Equipment.AcquireDefaultItem(Items.Actions.ItemTaskType.CraftPickupProduct, product.ItemId, product.Amount);
                    }
                    else
                    {
                        CancelCraft();
                        return;
                    }
                    */
                }
            }

            if (_count > 0 && !_craft.IsPack)
                Craft(_craft, _count, _doodadId);
            else
            {
                CancelCraft();
            }
        }

        // Not called for now. Needs to be called when crafting is cancelled.
        public void CancelCraft()
        {
            IsCrafting = false;
            _craft = null;
            _count = 0;
            _doodadId = 0;

            // Might want to send a packet here, I think there is a packet when crafting fails. Not sure yet..
        }
    }
}
