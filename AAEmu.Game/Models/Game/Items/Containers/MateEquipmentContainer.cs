using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Mate;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

// The Mate namespace sits beside this one, so the type needs naming apart from it.
using MateUnit = AAEmu.Game.Models.Game.Units.Mate;

namespace AAEmu.Game.Models.Game.Items.Containers;

public class MateEquipmentContainer : EquipmentContainer
{
    public MateEquipmentContainer(uint ownerId, SlotType containerType, bool createWithNewId, Unit parentUnit) : base(ownerId, containerType, createWithNewId, parentUnit)
    {
        // Fancy way of getting the last enum value + 1 for equipment slots
        ContainerSize = (int)Enum.GetValues<EquipmentItemSlot>().Max() + 1;
    }

    public override bool CanAccept(Item item, int targetSlot)
    {
        if (!base.CanAccept(item, targetSlot))
            return false;

        // A mate wears what its own npc's packs list (mate_equip_pack_groups → mate_equip_pack_items): a
        // mate's TemplateId is the npc it was summoned from, which is the key those tables use. A mate
        // whose npc carries no pack has no whitelist, and the tables say nothing rather than everything
        // being refused.
        if (item == null || ParentUnit is not MateUnit mate)
            return true;

        if (!MateGameData.Instance.MatePacksList(mate.TemplateId, item.TemplateId))
        {
            Logger.Warn(
                "{0} tried to put {1} ({2}) on {3}, whose packs do not list it",
                Owner?.Name, item.Template?.Name, item.TemplateId, mate.Name);
            return false;
        }

        // And only in a position its own kind of mate may wear at all: the npc's equip-slot pack says
        // which of the four (mate_equip_slot_packs), and the slot number is the EquipmentItemSlot the
        // container is indexed by.
        var packId = (mate.Template as NpcTemplate)?.MateEquipSlotPackId ?? 0;
        if (MateEquipSlots.ForEquipmentSlot(targetSlot) is { } position &&
            !MateGameData.Instance.MateWearsSlot((uint)packId, position))
        {
            Logger.Warn(
                "{0} tried to put {1} ({2}) on {3}'s {4}, which a mate of its kind does not wear",
                Owner?.Name, item.Template?.Name, item.TemplateId, mate.Name, position);
            return false;
        }

        return true;
    }

    public override void OnEnterContainer(Item item, ItemContainer lastContainer, byte previousSlot)
    {
        base.OnEnterContainer(item, lastContainer, previousSlot);

        // The request handler owns the single equipment-change reply.
    }

    public override void OnLeaveContainer(Item item, ItemContainer newContainer, byte previousSlot)
    {
        base.OnLeaveContainer(item, newContainer, previousSlot);

        // Reply owned by CSChangeMateEquipmentPacket — see OnEnterContainer.
    }
}
