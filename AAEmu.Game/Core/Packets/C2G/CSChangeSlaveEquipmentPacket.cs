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
/// CS 0x064 — equip / unequip / re-slot a ship (slave) part.
/// then num × { Item item1, Item item2, u8 slot1Type, u8 slot1, u8 slot2Type, u8 slot2, expireTime(s64) }.
///
/// Pairing is item1↔slot1 and item2↔slot2. slot2 is always a slave gear slot
/// (SlotType.EquipmentSlave, 0xF2); slot1 is either the bag (Inventory, 2) or a second slave slot
///
/// The client applies nothing locally: it locks both slot keys and waits for
/// the reply's <b>item2</b> into <b>inventory slot1</b> — so the reply must echo the pre-move
/// snapshot of both slots. The ship side of the change is driven by SCUnitEquipmentsChanged.
///
/// Ship parts are ItemDetailType.SlaveEquipment (10, 12-byte body); reading them as EquipItem
/// over-reads and misaligns the trailing slot bytes to None#0.
/// </summary>
public class CSChangeSlaveEquipmentPacket() : GamePacket(CSOffsets.CSChangeSlaveEquipmentPacket, 1)
{
    private sealed class SlaveEquipRequest
    {
        public Item WireItem1;
        public Item WireItem2;
        public SlotType Slot1Type;
        public byte Slot1;
        public SlotType Slot2Type;
        public byte Slot2;
        public DateTime ExpireTime;

        // Pre-move contents of slot1/slot2 as the server knows them, echoed back to the client.
        public Item EchoItem1;
        public Item EchoItem2;
    }

    public override void Read(PacketStream stream)
    {
        var characterId = (uint)stream.ReadUInt64();
        var slaveTl = stream.ReadUInt16();
        var dbSlaveId = stream.ReadUInt32();
        var bts = stream.ReadBoolean();
        var num = stream.ReadByte();
        if (num > 3)
            num = 3; // client array is 3 entries

        // Parse every entry before doing any work: each one needs a reply or the client leaves that
        // slot pair locked forever ("Can't equip; slot is locked.").
        var requests = new List<SlaveEquipRequest>();
        for (var i = 0; i < num; i++)
        {
            var request = new SlaveEquipRequest { WireItem1 = new Item(), WireItem2 = new Item() };
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
            "ChangeSlaveEquipment, Char: {0}, Tl: {1}, DbSlaveId: {2}, Bts: {3}, Num: {4}",
            characterId, slaveTl, dbSlaveId, bts, num);

        var character = Connection.ActiveChar;
        if (character == null || character.Id != characterId)
            return;

        var slave = ResolveSlave(character, slaveTl, dbSlaveId);
        var owned = slave?.Summoner != null && slave.Summoner.Id == character.Id;
        if (!owned)
        {
            Logger.Warn(
                "ChangeSlaveEquipment: {0} has no owned slave tl={1} db={2}",
                character.Name, slaveTl, dbSlaveId);
        }

        var replyTl = slave?.TlId ?? slaveTl;
        var replyDbId = slave?.Id ?? dbSlaveId;
        var anyApplied = false;

        foreach (var request in requests)
        {
            var success = owned && TryApply(character, slave, request);
            anyApplied |= success;

            character.SendPacket(new SCSlaveEquipmentChangedPacket(
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
                characterId,
                replyDbId,
                bts,
                success,
                request.ExpireTime));
        }

        if (!anyApplied)
            return;

        // Masts and plating carry MaxHealth, so swapping a part changes the hull's cap.
        slave.UpdateSlaveGearBonuses();
        slave.Hp = Math.Min(slave.Hp, slave.MaxHp);
        slave.BroadcastPacket(new SCUnitPointsPacket(slave.ObjId, slave.Hp, slave.Mp), false);
        character.SendPacket(new SCUnitPointsPacket(slave.ObjId, slave.Hp, slave.Mp));
    }

    private static Slave ResolveSlave(Character character, ushort slaveTl, uint dbSlaveId)
    {
        var slaveManager = character.ParentWorld?.SlaveManager;
        if (slaveManager == null)
            return null;

        Slave slave = null;
        if (slaveTl != 0)
            slave = slaveManager.FindSlaveByTlId(slaveTl);
        if (slave == null && dbSlaveId != 0)
            slave = slaveManager.FindSlaveByDbId(dbSlaveId);
        slave ??= slaveManager.GetActiveSlaveByOwnerObjId(character.ObjId);

        if (slave != null && dbSlaveId != 0 && slave.Id != 0 && slave.Id != dbSlaveId)
            return null;

        return slave;
    }

    private static bool TryApply(Character character, Slave slave, SlaveEquipRequest request)
    {
        // slot1 may be the bag or a second ship slot, slot2 is always a ship slot.
        var container1 = ResolveContainer(character, slave, request.Slot1Type);
        var container2 = request.Slot2Type == SlotType.EquipmentSlave ? slave.Equipment : null;
        if (container1 == null || container2 == null)
        {
            Logger.Warn(
                "ChangeSlaveEquipment: unsupported slots {0}#{1} <-> {2}#{3}",
                request.Slot1Type, request.Slot1, request.Slot2Type, request.Slot2);
            return false;
        }

        if (request.Slot1 >= container1.ContainerSize || request.Slot2 >= container2.ContainerSize)
        {
            Logger.Warn(
                "ChangeSlaveEquipment: slot out of range {0}#{1}/{2} <-> {3}#{4}/{5}",
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

        // The client tells us what it thinks is in both slots. Acting on a stale view is how an
        // unrelated bag item ends up swapped onto the ship, so refuse instead of guessing.
        if (!SlotMatches(request.WireItem1, item1) || !SlotMatches(request.WireItem2, item2))
        {
            Logger.Warn(
                "ChangeSlaveEquipment: slot contents mismatch, client {0}/{1} server {2}/{3} for {4}#{5} <-> {6}#{7}",
                request.WireItem1.TemplateId, request.WireItem2.TemplateId,
                item1?.TemplateId ?? 0, item2?.TemplateId ?? 0,
                request.Slot1Type, request.Slot1, request.Slot2Type, request.Slot2);
            return false;
        }

        if (item1 == null && item2 == null)
            return false;

        Logger.Debug(
            "ChangeSlaveEquipment entry: {0}#{1} tpl={2} <-> {3}#{4} tpl={5}",
            request.Slot1Type, request.Slot1, item1?.TemplateId ?? 0,
            request.Slot2Type, request.Slot2, item2?.TemplateId ?? 0);

        if (!Exchange(container1, request.Slot1, item1, container2, request.Slot2, item2))
        {
            Logger.Warn(
                "ChangeSlaveEquipment failed: {0}#{1} <-> {2}#{3}",
                request.Slot1Type, request.Slot1, request.Slot2Type, request.Slot2);
            return false;
        }

        RefreshVisual(character, slave, request.Slot1Type, request.Slot1);
        RefreshVisual(character, slave, request.Slot2Type, request.Slot2);
        return true;
    }

    private static ItemContainer ResolveContainer(Character character, Slave slave, SlotType slotType)
    {
        return slotType switch
        {
            SlotType.Inventory => character.Inventory.Bag,
            SlotType.EquipmentSlave => slave.Equipment,
            _ => null
        };
    }

    private static bool SlotMatches(Item wireItem, Item serverItem)
    {
        if (wireItem == null || wireItem.TemplateId == 0)
            return serverItem == null;

        if (serverItem == null || serverItem.TemplateId != wireItem.TemplateId)
            return false;

        // Ship parts handed out by the initial pack used to be created without an id.
        return wireItem.Id == 0 || serverItem.Id == wireItem.Id;
    }

    /// <summary>
    /// Moves item1 into slot2 and item2 into slot1. Equipment slots are addressed explicitly, so an
    /// occupied slot has to be vacated before it is filled — AddOrMoveExistingItem would otherwise
    /// leave two items claiming the same slot, of which only the first is ever found again.
    /// container2 is always the ship, so the detach below never runs on the player's bag.
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

        // Detached, so the re-add below does not fire a leave event for a slot that item1 has
        // meanwhile taken over.
        item2._holdingContainer = null;

        if (!container2.AddOrMoveExistingItem(ItemTaskType.Invalid, item1, slot2))
        {
            container2.AddOrMoveExistingItem(ItemTaskType.Invalid, item2, slot2);
            return false;
        }

        if (container1.AddOrMoveExistingItem(ItemTaskType.Invalid, item2, slot1))
            return true;

        // slot1 was just vacated by item1, so this should not happen. Park the item rather than
        // leaving it detached from every container.
        Logger.Error(
            "ChangeSlaveEquipment: could not place {0} in {1}#{2}",
            item2.TemplateId, container1.ContainerType, slot1);
        container1.AddOrMoveExistingItem(ItemTaskType.Invalid, item2);
        return false;
    }

    private static void RefreshVisual(Character character, Slave slave, SlotType slotType, byte slot)
    {
        if (slotType != SlotType.EquipmentSlave)
            return;

        character.ParentWorld?.SlaveManager?.SpawnOrReplaceEquipmentVisual(
            slave, slave.Equipment.GetItemBySlot(slot), slot, character);
    }
}
