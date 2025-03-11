using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSChangeMateEquipmentPacket : GamePacket
{
    public CSChangeMateEquipmentPacket() : base(CSOffsets.CSChangeMateEquipmentPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        // Owner PlayerId
        var owningPlayerId = stream.ReadUInt32();
        // Mate tl
        var mateTl = stream.ReadUInt16();
        // Should be Passenger PlayerId, but still reports 0 even when the seat is taken
        // Maybe this was planned to be used if somehow somebody else than the owner is equipping gear onto the mount
        var passengerPlayerId = stream.ReadUInt32();
        // Seems to be always 0
        var bts = stream.ReadBoolean();
        // Always 1 for 1 item at a time
        var itemCount = stream.ReadByte();

        Logger.Debug($"ChangeMateEquipment - TlId: {mateTl}, Owner: {owningPlayerId}, Id2: {passengerPlayerId}, BTS: {bts}, Count: {itemCount}");

        var character = Connection.ActiveChar;
        var mate = MateManager.Instance.GetActiveMateByTlId(character.ObjId, mateTl);
        if (mate == null)
        {
            Logger.Warn($"ChangeMateEquipment, Unable to find mate with tlId {mateTl}!");
            return;
        }

        if (itemCount == 0)
            return;

        // SlotType, SlotNum, Item
        for (var i = 0; i < itemCount; i++)
        {
            var playerItem = new ItemAndLocation();
            var mateItem = new ItemAndLocation();

            playerItem.Item = new EquipItem();
            playerItem.Item.Read(stream);

            mateItem.Item = new EquipItem();
            mateItem.Item.Read(stream);

            playerItem.SlotType = (SlotType)stream.ReadByte();
            playerItem.SlotNumber = stream.ReadByte();

            mateItem.SlotType = (SlotType)stream.ReadByte();
            mateItem.SlotNumber = stream.ReadByte();

            var expireTime = stream.ReadDateTime(); // add in 5+

            var isEquip = playerItem.Item.TemplateId != 0;

            // Override the Read data with the actual Item data
            var sourceContainer = character.Inventory.Bag;
            var targetContainer = mate.Equipment;
            playerItem.Item = (EquipItem)sourceContainer.GetItemBySlot(playerItem.SlotNumber);
            mateItem.Item = (EquipItem)targetContainer.GetItemBySlot(mateItem.SlotNumber);

            // Logger.Debug($"{playerItem.SlotType} #{playerItem.SlotNumber} ItemId:{playerItem.Item?.Id ?? 0} -> {mateItem.SlotType} #{mateItem.SlotNumber} ItemId:{mateItem.Item?.Id ?? 0}");
            // character.SendMessage($"MateEquip: {playerItem.SlotType} #{playerItem.SlotNumber} ItemId:{playerItem.Item?.Id ?? 0} -> {mateItem.SlotType} #{mateItem.SlotNumber} ItemId:{mateItem.Item?.Id ?? 0}");

            // If un-equipping, swap the items around
            if (!isEquip)
            {
                (playerItem, mateItem) = (mateItem, playerItem);
                (sourceContainer, targetContainer) = (targetContainer, sourceContainer);
            }

            //if (isEquip)
            if (playerItem.Item != null)
            {
                var res = character.Inventory.SplitOrMoveItemEx(ItemTaskType.Invalid,
                    sourceContainer, targetContainer,
                    playerItem.Item.Id, playerItem.SlotType, playerItem.SlotNumber,
                    0, mateItem.SlotType, mateItem.SlotNumber);

                // character.SendMessage($"SCMateEquipmentChanged - {(isEquip ? playerItem : mateItem)} -> {(isEquip ? mateItem : playerItem)}, MateTl: {mateTl} => Success {res}");
                if (!res)
                {
                    character.SendPacket(new SCMateEquipmentChangedPacket(
                        isEquip ? playerItem : mateItem,
                        isEquip ? mateItem : playerItem,
                        mateTl,
                        owningPlayerId, passengerPlayerId,
                        bts, res, expireTime));
                }
            }
        }
    }
}
