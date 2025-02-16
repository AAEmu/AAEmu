using System;
using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;

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
                stream.Read(playerItem.Item);

                slaveItem.Item = new Item();
                stream.Read(slaveItem.Item);

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
                    //if (!res)
                    {
                        character.SendPacket(new SCSlaveEquipmentChangedPacket(
                            isEquip ? playerItem : slaveItem,
                            isEquip ? slaveItem : playerItem,
                            slaveTl,
                            owningPlayerId, dbSlaveId,
                            bts, res, expireTime));
                    }

                    // TODO добавить удаление Slaves
                    if (!isEquip)
                    {
                        var slaveId = ItemManager.Instance.GetSlaveIdByItemId(playerItem.Item.TemplateId);
                        if (slaveId > 0)
                        {
                            DespawnSlave(slave, slaveId);
                        }
                        else
                        {
                            var doodadId = ItemManager.Instance.GetDoodadIdByItemId(playerItem.Item.TemplateId);
                            if (doodadId > 0)
                            {
                                DespawnDoodad(slave, doodadId);
                            }
                        }
                    }
                    else
                    {
                        var slaveId = ItemManager.Instance.GetSlaveIdByItemId(playerItem.Item.TemplateId);
                        if (slaveId > 0)
                        {
                            SpawnSlave(character, slave, playerItem, slaveId);
                        }
                        else
                        {
                            var doodadId = ItemManager.Instance.GetDoodadIdByItemId(playerItem.Item.TemplateId);
                            if (doodadId <= 0) { continue; }

                            SpawnDoodad(character, slave, playerItem, doodadId);
                        }
                    }
                }
            }
        }

        private static void DespawnDoodad(Slave slave, uint doodadId)
        {
            var doodad = slave.GetDoodadByItemTemplateId(doodadId);
            //doodad?.DoDespawn(doodad);
            doodad.IsPersistent = false;
            doodad.Despawn = DateTime.UtcNow;
            SpawnManager.Instance.AddDespawn(doodad);
            slave.AttachedDoodads.Remove(doodad);
        }

        private static void DespawnSlave(Slave slave, uint slaveId)
        {
            var attachedSlave = slave.GetSlaveByItemTemplateId(slaveId);
            WorldManager.Instance.RemoveObject(attachedSlave);
            attachedSlave.Despawn = DateTime.UtcNow;
            SpawnManager.Instance.AddDespawn(attachedSlave);
            slave.AttachedSlaves.Remove(attachedSlave);
        }

        private static void SpawnSlave(Character character, Slave slave, ItemAndLocation playerItem, uint slaveId)
        {
            var attachPoint = SlaveManager.Instance.GetAttachPointBySlotId(slave.TemplateId, (uint)playerItem.Item.Slot);
            var byteArray = new byte[12];
            Buffer.BlockCopy(BitConverter.GetBytes(slave.Hp), 0, byteArray, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(0ul), 0, byteArray, 4, 8);
            playerItem.Item.Detail = byteArray;
            playerItem.Item.DetailType = ItemDetailType.SlaveEquipment;
            playerItem.Item.DetailBytesLength = 12;
            playerItem.Item.ItemFlags = ItemFlag.SoulBound; // связанный
            playerItem.Item.ChargeUseSkillTime = DateTime.UtcNow;

            character.SendPacket(new SCUpdateSlaveSourceItemPacket(slave.ObjId, playerItem.Item.Id, slave.Hp, (byte)playerItem.Item.Slot));
            var slaveBinding = new SlaveBindings
            {
                Id = 0,
                OwnerId = slave.TemplateId,
                OwnerType = "Slave",
                SlaveId = slaveId,
                AttachPointId = attachPoint
            };
            SlaveManager.Instance.SpawnSlaveSlaves(character, slaveBinding, slave);
        }

        private static void SpawnDoodad(Character character, Slave slave, ItemAndLocation playerItem, uint doodadId)
        {
            // Send Item manipulation packet 
            character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.DoodadCreate, [], [], 20));

            var attachPoint2 = SlaveManager.Instance.GetAttachPointBySlotId(slave.TemplateId, (uint)playerItem.Item.Slot);
            var doodadBinding = new SlaveDoodadBindings
            {
                Id = 0,
                OwnerId = slave.TemplateId,
                OwnerType = "Slave",
                DoodadId = doodadId,
                Persist =  true, // будем ли сохранять в базе
                Scale = 1f,
                AttachPointId = attachPoint2
            };

            // Create all the trinkets that have been downloaded from inventory.
            SlaveManager.Instance.CreateSlaveDoodads(character, playerItem.Item, slave, doodadBinding);
        }
    }
}
