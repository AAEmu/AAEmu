using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
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
            {
                CancelCraft();
                return;
            }

            if (Owner.Inventory.FreeSlotCount(SlotType.Inventory) < _craft.CraftProducts.Count)
            {
                CancelCraft();
                return;
            }

            foreach (var product in _craft.CraftProducts)
            {
                // Check if we're crafting a tradepack, if so, try to remove currently equipped backpack slot
                if (ItemManager.Instance.IsAutoEquipTradePack(product.ItemId) == false)
                {
                    Owner.Inventory.Bag.AcquireDefaultItem(Items.Actions.ItemTaskType.CraftPickupProduct,
                        product.ItemId, product.Amount, -1, Owner.Id);
                }
                else
                {
                    if (!Owner.Inventory.TryEquipNewBackPack(Items.Actions.ItemTaskType.CraftPickupProduct, product.ItemId, product.Amount,-1,Owner.Id))
                    {
                        CancelCraft();
                        return;
                    }
                }
            }

            foreach (var material in _craft.CraftMaterials)
            {
                Owner.Inventory.Bag.ConsumeItem(Items.Actions.ItemTaskType.CraftActSaved, material.ItemId, material.Amount,null);
            }

            if (_count > 0 && !_craft.IsPack)
                Craft(_craft, _count, _doodadId);
            else
            {
                CancelCraft();
            }
        }

        public void CancelCraft()
        {
            IsCrafting = false;
            _craft = null;
            _count = 0;
            _doodadId = 0;
            
            // Also cancel the related skill ? I don't think this really does anything for crafts, but can't hurt I guess
            if (Owner != null)
            {
                if (Owner.SkillTask != null)
                    Owner.SkillTask.Skill.Cancelled = true;
                Owner.InterruptSkills();
            }

            // Might want to send a packet here, I think there is a packet when crafting fails. Not sure yet..
        }
    }
}
