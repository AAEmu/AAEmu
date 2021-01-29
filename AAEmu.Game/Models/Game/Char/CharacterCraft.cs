using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
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

        private bool isTradePack;

        public CharacterCraft(Character owner) => Owner = owner;

        public void Craft(Craft craft, int count, uint doodadId)
        {
            _craft = craft;
            _count = count;
            _doodadId = doodadId;

            var hasMaterials = true;

            foreach (var craftMaterial in craft.CraftMaterials)
            {
                if (Owner.Inventory.GetItemsCount(craftMaterial.ItemId, craftMaterial.RequiredGrade) < craftMaterial.Amount)
                    hasMaterials = false;
            }

            // Check if is a trade pack
            foreach (var product in _craft.CraftProducts)
            {
                if (ItemManager.Instance.IsAutoEquipTradePack(product.ItemId)) {
                    isTradePack = true;
                    break;
                }
            }

            if (isTradePack)
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

            var productGrade = -1;

            foreach (var material in _craft.CraftMaterials)
            {
                Item itemToConsume = null;

                // Consume the right item if requires a grade
                if (material.RequiredGrade >= 0)
                    itemToConsume = Owner.Inventory.Bag.GetFirstItemByTemplateId(material.ItemId, material.RequiredGrade);


                // Check if one of the materials is the item to take the grade from
                if (material.MainGrade)
                {
                    if (itemToConsume != null)
                        productGrade = itemToConsume.Grade;
                    else
                        productGrade = Owner.Inventory.Bag.GetFirstItemByTemplateId(material.ItemId).Grade;
                }

                Owner.Inventory.Bag.ConsumeItem(Items.Actions.ItemTaskType.CraftActSaved, material.ItemId, material.Amount, itemToConsume);
            }

            foreach (var product in _craft.CraftProducts)
            {
                // Check if we're crafting a tradepack, if so, try to remove currently equipped backpack slot
                if (ItemManager.Instance.IsAutoEquipTradePack(product.ItemId) == false)
                {
                    // Product has a set grade
                    if (product.UseGrade)
                    {
                        productGrade = product.ItemGradeId;
                    } else if (productGrade != -1 && ItemManager.Instance.GetTemplate(product.ItemId) is WeaponTemplate && DoGradeUpgradeRoll(product.ItemId))
                    {   
                        // Do grade upgrade if possible
                        productGrade = GetNextGrade(ItemManager.Instance.GetGradeTemplate(productGrade), 1).Grade;
                    }

                    Owner.Inventory.Bag.AcquireDefaultItem(Items.Actions.ItemTaskType.CraftPickupProduct,
                        product.ItemId, product.Amount, productGrade, Owner.Id);
                }
                else
                {
                    if (!Owner.Inventory.TryEquipNewBackPack(Items.Actions.ItemTaskType.CraftPickupProduct, product.ItemId, product.Amount,-1,Owner.Id))
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

            if (_count > 0 && !isTradePack)
            {
                Craft(_craft, _count, _doodadId);
            }
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
        }

        // This existed in the game but can't find it anywhere in the DB
        private bool DoGradeUpgradeRoll(uint itemTemplateId)
        {
            if (!IsWeaponUpgradeable(itemTemplateId))
                return false;

            var successRoll = Rand.Next(0, 10000);
            var successChance = 5000; // 50% for test

            return successRoll < successChance;
        }

        public bool IsWeaponUpgradeable(uint itemTemplateId)
        {
            var template = ItemManager.Instance.GetTemplate(itemTemplateId);

            return (template != null) && (template is WeaponTemplate)
                && template.FixedGrade == -1 && template.Gradable;
        }
        private GradeTemplate GetNextGrade(GradeTemplate currentGrade, int gradeChange)
        {
            return ItemManager.Instance.GetGradeTemplateByOrder(currentGrade.GradeOrder + gradeChange);
        }
    }
}
