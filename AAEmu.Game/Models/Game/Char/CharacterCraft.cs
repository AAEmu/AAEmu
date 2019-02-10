

using System.Collections.Generic;
using AAEmu.Game.Core.Helper;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterCraft {
        private Character owner {get; set;}
        
        private int count {get; set;}
        private Craft craft {get; set;}
        private uint doodadId {get; set;}
        
        public bool IsCrafting = false;

        public CharacterCraft(Character owner) => this.owner = owner;

        public void Craft(Craft craft, int count, uint doodadId) {
            this.craft = craft;
            this.count = count;
            this.doodadId = doodadId;

            bool hasMaterials = true;

            List<CraftMaterial> craftMaterials = CraftManager.Instance.GetMaterialsForCraft(craft);
            CraftProduct craftProduct = CraftManager.Instance.GetResultForCraft(craft);

            foreach(CraftMaterial craftMaterial in craftMaterials) {
                Item materialItem = owner.Inventory.GetItemByTemplateId(craftMaterial.ItemId);
                if (materialItem == null || materialItem.Count < craftMaterial.Amount) {
                    hasMaterials = false;
                }
            }
            
            if (hasMaterials) {
                this.IsCrafting = true;

                var caster = SkillCaster.GetByType(SkillCasterType.Unit);
                caster.ObjId = owner.ObjId;

                var target = SkillCastTarget.GetByType(SkillCastTargetType.Doodad);
                target.ObjId = doodadId;

                var skill = new Skill(SkillManager.Instance.GetSkillTemplate(craft.SkillId));
                skill.Use(owner, caster, target);
            }
        }

        public void EndCraft() {
            count--;
            this.IsCrafting = false;
            
            List<CraftMaterial> materials = CraftManager.Instance.GetMaterialsForCraft(craft);
            CraftProduct product = CraftManager.Instance.GetResultForCraft(craft);
            
            foreach(CraftMaterial material in materials) {
                Item materialItem = owner.Inventory.GetItemByTemplateId(material.ItemId);
                InventoryHelper.RemoveItemAndUpdateClient(owner, materialItem, material.Amount);
            }

            Item resultItem = ItemManager.Instance.Create(product.ItemId, product.Amount, 0);
            InventoryHelper.AddItemAndUpdateClient(owner, resultItem);

            if (count > 0) {
                Craft(craft, count, doodadId);
            } else {
                this.craft = null;
                this.count = 0;
                this.doodadId = 0;
            }
        }

        // Not called for now. Needs to be called when crafting is cancelled.
        public void CancelCraft() {
            this.IsCrafting = false;
            this.craft = null;
            this.count = 0;
            this.doodadId = 0;

            // Might want to send a packet here, I think there is a packet when crafting fails. Not sure yet..
        }
    }
}
