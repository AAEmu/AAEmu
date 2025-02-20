using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class GiveCashPoint : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.GiveCashPoint;

    public override void Execute(BaseUnit caster,
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
        if (caster is Character) { Logger.Debug("Special effects: GiveCashPoint value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4); }

        if (caster is Character character)
        {
            //skillObject.
            //character.Equipment.RemoveItem(ItemTaskType.ConsumeSkillSource, casterObj.
            if (casterObj is SkillItem skillItem)
            {
                if (character.Inventory.Bag.ConsumeItem(ItemTaskType.ConsumeSkillSource, skillItem.ItemTemplateId, 1, null) > 0)
                {
                    if (!AccountManager.Instance.AddCredits(character.AccountId, value1))
                    {
                        Logger.Error($"Failed to credit Account:{character.AccountId} with {value1} credits.");
                    }
                    else
                    {
                        // TODO: Proper message?
                        character.SendMessage($"You received {value1} credits.");
                    }
                }
                var points = AccountManager.Instance.GetAccountDetails(character.AccountId);
                character.SendPacket(new SCICSCashPointPacket(points.Credits));
            }
        }
    }
}
