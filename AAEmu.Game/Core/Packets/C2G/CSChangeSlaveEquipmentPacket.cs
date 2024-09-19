using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeSlaveEquipmentPacket : GamePacket
    {
        public CSChangeSlaveEquipmentPacket() : base(CSOffsets.CSChangeSlaveEquipmentPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Owner PlayerId
            var owningPlayerId = stream.ReadUInt32();
            // Slave tl
            var slaveTl = stream.ReadUInt16();
            // dbSlaveId = 0
            var dbSlaveId = stream.ReadUInt32();
            // Seems to be always 0
            var bts = stream.ReadBoolean();
            // num Always 1 for 1 item at a time
            var itemCount = stream.ReadByte();

            Logger.Debug($"ChangeSlaveEquipment - TlId: {slaveTl}, Owner: {owningPlayerId}, dbSlaveId: {dbSlaveId}, BTS: {bts}, Count: {itemCount}");

            var character = Connection.ActiveChar;
            var slave = SlaveManager.Instance.GetSlaveByTlId(slaveTl);
            if (slave == null)
            {
                Logger.Warn($"ChangeSlaveEquipment, Unable to find slave with tlId {slaveTl}!");
                return;
            }

            if (itemCount == 0)
                return;

            for (var i = 0; i < itemCount; i++)
            {
                // SlotType, SlotNum, Item
                var playerItem = new ItemAndLocation();
                var slaveItem = new ItemAndLocation();

                playerItem.Item = new Item();
                playerItem.Item.Read(stream);

                slaveItem.Item = new Item();
                slaveItem.Item.Read(stream);

                playerItem.SlotType = (SlotType)stream.ReadByte();
                playerItem.SlotNumber = stream.ReadByte();

                slaveItem.SlotType = (SlotType)stream.ReadByte();
                slaveItem.SlotNumber = stream.ReadByte();

                var expireTime = stream.ReadDateTime(); // add in 5+

                var isEquip = playerItem.Item.TemplateId != 0;

                // Override the Read data with the actual Item data
                var sourceContainer = character.Inventory.Bag;
                var targetContainer = slave.Equipment;
                playerItem.Item = sourceContainer.GetItemBySlot(playerItem.SlotNumber);
                slaveItem.Item = targetContainer.GetItemBySlot(slaveItem.SlotNumber);

                // Logger.Debug($"{playerItem.SlotType} #{playerItem.SlotNumber} ItemId:{playerItem.Item?.Id ?? 0} -> {mateItem.SlotType} #{mateItem.SlotNumber} ItemId:{mateItem.Item?.Id ?? 0}");
                // character.SendMessage($"MateEquip: {playerItem.SlotType} #{playerItem.SlotNumber} ItemId:{playerItem.Item?.Id ?? 0} -> {mateItem.SlotType} #{mateItem.SlotNumber} ItemId:{mateItem.Item?.Id ?? 0}");

                // If un-equipping, swap the items around
                if (!isEquip)
                {
                    (playerItem, slaveItem) = (slaveItem, playerItem);
                    (sourceContainer, targetContainer) = (targetContainer, sourceContainer);
                }

                //if (isEquip)
                if (playerItem.Item != null)
                {
                    var res = character.Inventory.SplitOrMoveItemEx(ItemTaskType.Invalid,
                        sourceContainer, targetContainer,
                        playerItem.Item.Id, playerItem.SlotType, playerItem.SlotNumber,
                        0, slaveItem.SlotType, slaveItem.SlotNumber);

                    // character.SendMessage($"SCMateEquipmentChanged - {(isEquip ? playerItem : mateItem)} -> {(isEquip ? mateItem : playerItem)}, MateTl: {mateTl} => Success {res}");
                    if (!res)
                    {
                        character.SendPacket(new SCMateEquipmentChangedPacket(
                            isEquip ? playerItem : slaveItem,
                            isEquip ? slaveItem : playerItem,
                            slaveTl,
                            owningPlayerId, dbSlaveId,
                            bts, res, expireTime));
                    }
                }
            }
        }
    }
}
