using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
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
        if (sourceItem == null ||
            equipItem?.Template is not EquipItemTemplate equipTemplate ||
            equipTemplate.RechargeRestrictItemId == 0 ||
            equipTemplate.RechargeRestrictItemId != sourceItem.TemplateId)
        {
            Logger.Warn(
                "RechargeItemBuff rejected owner={0} source={1} target={2}",
                owner.Id,
                sourceCaster.ItemId,
                itemTarget.Id);
            return;
        }

        // Native validation rejects recharge while the target occupies an equipment slot. The
        // bag detail is charged first; its normal equip path applies RechargeBuffId afterwards.
        if (equipItem.SlotType == SlotType.Equipment)
        {
            owner.SendErrorMessage(ErrorMessageType.CannotRechargeWhenEquipped);
            return;
        }

        equipItem.ChargeStartTime = time;
        equipItem.ChargeCount = equipTemplate.ChargeCount;

        owner.SendPacket(new SCItemTaskSuccessPacket(
            ItemTaskType.RechargeBuff,
            [new ItemUpdate(equipItem)],
            []));
    }
}
