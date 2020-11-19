using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Game.Char
{
    public partial class Character
    {
        public void UpdateGearBonuses()
        {
            // We use index 1 for gear bonuses. Will make this a constant later, or do it properly. Right now the expected behavior is to have key == buff id, which doesn't work when you have items.
            Bonuses[1] = new List<Bonus>();

            foreach (var item in Equipment.Items)
            {
                if (!(item is EquipItem ei))
                    continue;

                // Gems
                foreach (var gem in ei.GemIds)
                foreach (var template in ItemManager.Instance.GetUnitModifiers(gem))
                    AddBonus(1, new Bonus {Template = template, Value = template.Value});
            }
            
            // Compute equip effects ?
            
            // Compute gear buff
            ApplyArmorGradeBuff();
        }

        private void ApplyArmorGradeBuff()
        {
            // Clear any existing armor grade buffs
            Effects.RemoveBuffs(145, 1);
            
            // Get armor pieces by kind
            var armorPieces = new Dictionary<ArmorType, List<Armor>>();
            foreach (var item in Equipment.Items)
            {
                if (!(item is Armor armor))
                    continue;

                if (!(item.Template is ArmorTemplate armorTemplate))
                    continue;

                if (!armorPieces.ContainsKey((ArmorType)armorTemplate.KindTemplate.TypeId))
                    armorPieces.Add((ArmorType)armorTemplate.KindTemplate.TypeId, new List<Armor>());
                armorPieces[(ArmorType)armorTemplate.KindTemplate.TypeId].Add(armor);
            }

            // Get kind with most pieces
            var piecesOfKind = armorPieces.First();
            foreach (var piecesByKind in armorPieces)
            {
                if (piecesByKind.Value.Count > piecesOfKind.Value.Count) piecesOfKind = piecesByKind;
            }

            var piecesToAccountForBuff = piecesOfKind.Value;

            if (piecesToAccountForBuff.Count < 4)
                return;
            
            // Get only pieces >= arcane
            var piecesAboveArcane = piecesToAccountForBuff.Where(p => p.Grade >= (int) ItemGrade.Arcane).ToList();
            if (piecesAboveArcane.Count < 4)
                return;

            var totalLevel = piecesAboveArcane.Sum(a => a.Template.Level);

            // This const was calculated by hand, it might make no sense.
            var abLevel = totalLevel * 0.40670554f;
            var gradeBuffAbLevel = (abLevel * abLevel) / 15 + 30;
            var lowestGrade = piecesAboveArcane.Min(a => a.Grade);
            
            // Apply buff 
            if (piecesAboveArcane.First().Template is ArmorTemplate armorTemp)
            {
                var type = armorTemp.WearableTemplate.TypeId;
                var armorGradeBuff = ItemManager.Instance.GetArmorGradeBuff((ArmorType)type, (ItemGrade) lowestGrade);
                var buffTemplate = SkillManager.Instance.GetBuffTemplate(armorGradeBuff.BuffId);
                
                var newEffect =
                    new Effect(this, this, new SkillCasterUnit(), buffTemplate, null, DateTime.Now)
                    {
                        AbLevel = (uint) gradeBuffAbLevel
                    };

                Effects.AddEffect(newEffect);
            }
        }
    }
}
