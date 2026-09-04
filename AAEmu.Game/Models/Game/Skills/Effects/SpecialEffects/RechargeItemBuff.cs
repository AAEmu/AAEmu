using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class RechargeItemBuff : SpecialEffectAction
{
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
        if (caster is not Character owner ||
            casterObj is not SkillItem sourceCaster ||
            targetObj is not SkillCastItemTarget itemTarget)
            return;

        // Type 95 has no data parameters. The selected equipment declares both the permitted
        // reagent and the resulting charge; resolve both item instances from this character's
        // inventory so client-supplied template ids cannot choose either side of the operation.
        var sourceItem = owner.Inventory.GetItemById(sourceCaster.ItemId);
        var equipItem = owner.Inventory.GetItemById(itemTarget.Id) as EquipItem;
        switch (ItemChargeRules.TryApply(equipItem, sourceItem, time))
        {
            case ItemChargeRules.RechargeApply.Rejected:
                Logger.Warn(
                    "RechargeItemBuff rejected owner={0} source={1} target={2}",
                    owner.Id,
                    sourceCaster.ItemId,
                    itemTarget.Id);
                return;
            case ItemChargeRules.RechargeApply.Equipped:
                owner.SendErrorMessage(ErrorMessageType.CannotRechargeWhenEquipped);
                return;
        }

        // Same publish as temper / synthesis: the detail packet is what the client redraws from.
        // An UpdateDetail item task is a length-prefixed array the client does not treat as a
        // detail, which is why a successful lure apply left the rod as an invalid item with no
        // mesh in hand.
        owner.SendPacket(new SCItemDetailUpdatedPacket(equipItem));
        owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.RechargeBuff, [], []));
    }
}
