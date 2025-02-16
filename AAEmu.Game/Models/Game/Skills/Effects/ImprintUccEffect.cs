using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class ImprintUccEffect : EffectTemplate
{
    public uint ItemId { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Trace("ImprintUccEffect");
        if (!(target is Character player))
            return;
        if (!(casterObj is SkillItem skillItem))
            return;
        if (!(targetObj is SkillCastItemTarget scit))
            return;
        var stampItem = ItemManager.Instance.GetItemByItemId(skillItem.ItemId);
        var targetItem = ItemManager.Instance.GetItemByItemId(scit.Id);

        // TODO: Check if items are owned by caster

        if ((stampItem != null) && (targetItem != null))
        {
            //var oldFlags = targetItem.ItemFlags;
            UccManager.ApplyStamp(stampItem, targetItem);
            // Send Item Ucc changed packet
            player.SendPacket(new SCItemUccDataChangedPacket(stampItem.UccId, player.Id, targetItem.Id));
            // Send ItemTask to change flags on client
            player.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.CreateOriginUcc, new ItemUpdateBits(targetItem), null));
            // Consume the stamp
            // Retail seems to use QuestRemoveSupplies (39) for this instead of ImprintUcc
            //stampItem._holdingContainer.ConsumeItem(ItemTaskType.ImprintUcc, stampItem.TemplateId,1, stampItem);
        }
        else
        {
            Logger.Warn("ImprintUccEffect: Invalid item reference");
        }
    }
}
