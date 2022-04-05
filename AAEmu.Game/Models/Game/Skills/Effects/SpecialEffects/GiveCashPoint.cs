using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class GiveCashPoint : SpecialEffectAction
    {
        protected override SpecialType SpecialEffectActionType => SpecialType.GiveCashPoint;

        public override void Execute(Unit caster,
            SkillCaster casterObj,
            BaseUnit target,
            SkillCastTarget targetObj,
            CastAction castObj,
            Skill skill,
            SkillObject skillObject,
            DateTime time,
            int value1,
            int value2,
            int value3,
            int value4)
        {
            if (caster is Character) { _log.Debug("Special effects: GiveCashPoint value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4); }

            if (caster is Character character)
            {
                //skillObject.
                //character.Equipment.RemoveItem(ItemTaskType.ConsumeSkillSource, casterObj.
                if (casterObj is SkillItem skillItem)
                {
                    if (character.Inventory.Bag.ConsumeItem(ItemTaskType.ConsumeSkillSource, skillItem.ItemTemplateId, 1, null) > 0)
                    {
                        if (!CashShopManager.Instance.AddCredits(character.AccountId, value1))
                            _log.Error("Failed to credit Account:{0} with {1} credits.", character.AccountId, value1);
                        else
                            character.SendMessage("You received {0} credits.", value1);
                    }
                    var points = CashShopManager.Instance.GetAccountCredits(character.AccountId);
                    character.SendPacket(new SCICSCashPointPacket(points));
                }
            }
        }
    }
}
