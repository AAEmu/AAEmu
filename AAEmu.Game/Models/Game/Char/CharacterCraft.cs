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
                var materialItem = Owner.Inventory.GetItemByTemplateId(craftMaterial.ItemId);
                if (materialItem == null || materialItem.Count < craftMaterial.Amount)
                {
                    hasMaterials = false;
                }
            }

            if (_craft.IsPack)
            {
                var item = Owner.Inventory.GetItem(SlotType.Equipment, (byte)EquipmentItemSlot.Backpack);
                var backpackTemplate = (BackpackTemplate)item?.Template;
                if (backpackTemplate != null && backpackTemplate.BackpackType != BackpackType.Glider)
                {
                    // TODO mb check to drop glider to inventory
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

            if (Owner.Inventory.CountFreeSlots(SlotType.Inventory) < _craft.CraftProducts.Count)
                return;

            foreach (var material in _craft.CraftMaterials)
            {
                var materialItem = Owner.Inventory.GetItemByTemplateId(material.ItemId);
                InventoryHelper.RemoveItemAndUpdateClient(Owner, materialItem, material.Amount);
            }

            foreach (var product in _craft.CraftProducts)
            {
                if (!_craft.IsPack)
                {
                    var resultItem = ItemManager.Instance.Create(product.ItemId, product.Amount, 0);
                    InventoryHelper.AddItemAndUpdateClient(Owner, resultItem);
                }
                else
                {
                    // Remove player backpack
                    Owner.Inventory.TakeoffBackpack();
                    // Put tradepack in their backpack slot
                    var resultItem = ItemManager.Instance.Create(product.ItemId, product.Amount, 0);
                    InventoryHelper.AddItemAndUpdateClient(Owner, resultItem);
                    Owner.Inventory.Move(resultItem.Id, resultItem.SlotType, (byte)resultItem.Slot, 0,
                        SlotType.Equipment, (byte)EquipmentItemSlot.Backpack);
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
