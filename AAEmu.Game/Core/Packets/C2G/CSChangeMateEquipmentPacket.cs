using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Equips or unequips mate gear using the same item-pair exchange rules as slave equipment.
/// The client locks both slots until SCMateEquipmentChanged unlocks them; that reply
/// must echo the pre-move snapshot.
/// </summary>
public class CSChangeMateEquipmentPacket() : GamePacket(CSOffsets.CSChangeMateEquipmentPacket, 1)
{
    private sealed class MateEquipRequest
    {
        public Item WireItem1;
        public Item WireItem2;
        public SlotType Slot1Type;
        public byte Slot1;
        public SlotType Slot2Type;
        public byte Slot2;
        public DateTime ExpireTime;
        public Item EchoItem1;
        public Item EchoItem2;
    }

    public override void Read(PacketStream stream)
    {
        var owningPlayerId = (uint)stream.ReadUInt64();
        var mateTl = stream.ReadUInt16();
        var passengerPlayerId = stream.ReadUInt32();
        var bts = stream.ReadBoolean();
        var itemCount = stream.ReadByte();
        if (itemCount > 2)
            itemCount = 2;

        var requests = new List<MateEquipRequest>();
        for (var i = 0; i < itemCount; i++)
        {
            var request = new MateEquipRequest
            {
                WireItem1 = new EquipItem(),
                WireItem2 = new EquipItem()
            };
            request.WireItem1.Read(stream);
            request.WireItem2.Read(stream);
            request.Slot1Type = (SlotType)stream.ReadByte();
            request.Slot1 = stream.ReadByte();
            request.Slot2Type = (SlotType)stream.ReadByte();
            request.Slot2 = stream.ReadByte();
            request.ExpireTime = stream.ReadDateTime();
            request.EchoItem1 = request.WireItem1.TemplateId != 0 ? request.WireItem1 : null;
            request.EchoItem2 = request.WireItem2.TemplateId != 0 ? request.WireItem2 : null;
            requests.Add(request);
        }

        Logger.Debug(
            "CSChangeMateEquipmentPacket - TlId: {0}, Owner: {1}, Id2: {2}, BTS: {3}, Count: {4}",
            mateTl, owningPlayerId, passengerPlayerId, bts, itemCount);

        var character = Connection.ActiveChar;
        if (character == null || character.Id != owningPlayerId)
            return;

        var mate = character.ParentWorld?.MateManager.GetActiveMateByTlId(mateTl);
        var owned = mate != null && mate.OwnerObjId == character.ObjId;
        if (!owned)
        {
            Logger.Warn(
                "ChangeMateEquipment: {0} has no owned mate tlId={1}",
                character.Name, mateTl);
        }

        var replyTl = mate?.TlId ?? mateTl;
        var anyApplied = false;

        foreach (var request in requests)
        {
            var success = owned && TryApply(character, mate, request);
            anyApplied |= success;
            character.SendPacket(new SCMateEquipmentChangedPacket(
                new ItemAndLocation
                {
                    Item = request.EchoItem1,
                    SlotType = request.Slot1Type,
                    SlotNumber = request.Slot1
                },
                new ItemAndLocation
                {
                    Item = request.EchoItem2,
                    SlotType = request.Slot2Type,
                    SlotNumber = request.Slot2
                },
                replyTl,
                owningPlayerId,
                passengerPlayerId,
                bts,
                success,
                request.ExpireTime));
        }

        if (!anyApplied)
            return;

        // Pet armor carries MaxHealth; SCUnitPoints is current only — refresh UnitState for MaxHp.
        mate.UpdateGearBonuses(null, null);
        character.SendPacket(new SCUnitStatePacket(mate));
        mate.BroadcastPacket(new SCUnitPointsPacket(mate.ObjId, mate.Hp, mate.Mp), false);
        character.SendPacket(new SCUnitPointsPacket(mate.ObjId, mate.Hp, mate.Mp));
        WorldIntegration.RelayUnitPointsToZone?.Invoke(mate.ObjId, mate.Hp, mate.Mp);
    }

    private static bool TryApply(Character character, Mate mate, MateEquipRequest request)
    {
        var container1 = ResolveContainer(character, mate, request.Slot1Type);
        var container2 = request.Slot2Type == SlotType.EquipmentMate ? mate.Equipment : null;
        if (container1 == null || container2 == null)
        {
            Logger.Warn(
                "ChangeMateEquipment: unsupported slots {0}#{1} <-> {2}#{3}",
                request.Slot1Type, request.Slot1, request.Slot2Type, request.Slot2);
            return false;
        }

        if (request.Slot1 >= container1.ContainerSize || request.Slot2 >= container2.ContainerSize)
        {
            Logger.Warn(
                "ChangeMateEquipment: slot out of range {0}#{1}/{2} <-> {3}#{4}/{5}",
                request.Slot1Type, request.Slot1, container1.ContainerSize,
                request.Slot2Type, request.Slot2, container2.ContainerSize);
            return false;
        }

        if (ReferenceEquals(container1, container2) && request.Slot1 == request.Slot2)
            return false;

        var item1 = container1.GetItemBySlot(request.Slot1);
        var item2 = container2.GetItemBySlot(request.Slot2);
        request.EchoItem1 = item1;
        request.EchoItem2 = item2;

        if (!SlotMatches(request.WireItem1, item1) || !SlotMatches(request.WireItem2, item2))
        {
            Logger.Warn(
                "ChangeMateEquipment: slot contents mismatch, client {0}/{1} server {2}/{3} for {4}#{5} <-> {6}#{7}",
                request.WireItem1.TemplateId, request.WireItem2.TemplateId,
                item1?.TemplateId ?? 0, item2?.TemplateId ?? 0,
                request.Slot1Type, request.Slot1, request.Slot2Type, request.Slot2);
            return false;
        }

        if (item1 == null && item2 == null)
            return false;

        Logger.Debug(
            "ChangeMateEquipment entry: {0}#{1} tpl={2} <-> {3}#{4} tpl={5}",
            request.Slot1Type, request.Slot1, item1?.TemplateId ?? 0,
            request.Slot2Type, request.Slot2, item2?.TemplateId ?? 0);

        if (!Exchange(container1, request.Slot1, item1, container2, request.Slot2, item2))
        {
            Logger.Warn(
                "ChangeMateEquipment failed: {0}#{1} <-> {2}#{3}",
                request.Slot1Type, request.Slot1, request.Slot2Type, request.Slot2);
            return false;
        }

        return true;
    }

    private static ItemContainer ResolveContainer(Character character, Mate mate, SlotType slotType)
    {
        return slotType switch
        {
            SlotType.Inventory => character.Inventory.Bag,
            SlotType.EquipmentMate => mate.Equipment,
            _ => null
        };
    }

    private static bool SlotMatches(Item wireItem, Item serverItem)
    {
        if (wireItem == null || wireItem.TemplateId == 0)
            return serverItem == null;

        if (serverItem == null || serverItem.TemplateId != wireItem.TemplateId)
            return false;

        return wireItem.Id == 0 || serverItem.Id == wireItem.Id;
    }

    /// <summary>
    /// Moves item1 into slot2 and item2 into slot1. Same vacate-before-fill path as
    /// CSChangeSlaveEquipmentPacket.Exchange — occupied mate slots cannot AddOrMove over each other.
    /// </summary>
    private static bool Exchange(
        ItemContainer container1, byte slot1, Item item1,
        ItemContainer container2, byte slot2, Item item2)
    {
        if (item2 == null)
            return container2.AddOrMoveExistingItem(ItemTaskType.Invalid, item1, slot2);

        if (item1 == null)
            return container1.AddOrMoveExistingItem(ItemTaskType.Invalid, item2, slot1);

        if (!container1.CanAccept(item2, slot1) || !container2.CanAccept(item1, slot2))
            return false;

        if (!container2.RemoveItem(ItemTaskType.Invalid, item2, false))
            return false;

        item2._holdingContainer = null;

        if (!container2.AddOrMoveExistingItem(ItemTaskType.Invalid, item1, slot2))
        {
            container2.AddOrMoveExistingItem(ItemTaskType.Invalid, item2, slot2);
            return false;
        }

        if (container1.AddOrMoveExistingItem(ItemTaskType.Invalid, item2, slot1))
            return true;

        Logger.Error(
            "ChangeMateEquipment: could not place {0} in {1}#{2}",
            item2.TemplateId, container1.ContainerType, slot1);
        container1.AddOrMoveExistingItem(ItemTaskType.Invalid, item2);
        return false;
    }
}
