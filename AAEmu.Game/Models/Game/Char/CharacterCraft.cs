using AAEmu.Game.Core.Helper;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Skills;

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

            foreach (var material in _craft.CraftMaterials)
            {
                var materialItem = Owner.Inventory.GetItemByTemplateId(material.ItemId);
                InventoryHelper.RemoveItemAndUpdateClient(Owner, materialItem, material.Amount);
            }

            foreach (var product in _craft.CraftProducts)
            {
                var resultItem = ItemManager.Instance.Create(product.ItemId, product.Amount, 0);
                InventoryHelper.AddItemAndUpdateClient(Owner, resultItem);
            }

            if (_count > 0)
                Craft(_craft, _count, _doodadId);
            else
            {
                _craft = null;
                _count = 0;
                _doodadId = 0;
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
