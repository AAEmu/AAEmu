using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Utils;
using AAEmu.Game.Models.Game.Items.Actions;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCreateDoodadPacket : GamePacket
    {
        public CSCreateDoodadPacket() : base(CSOffsets.CSCreateDoodadPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();
            var x = Helpers.ConvertLongX(stream.ReadInt64());
            var y = Helpers.ConvertLongY(stream.ReadInt64());
            var z = stream.ReadSingle();
            var zRot = stream.ReadSingle();
            var scale = stream.ReadSingle();
            var itemId = stream.ReadInt64();

            _log.Warn("CreateDoodad, Id: {0}, X: {1}, Y: {2}, Z: {3}, zRot: {4}  ItemId: {5}", id, x, y, z, zRot, itemId);


            var doodadSpawner = new DoodadSpawner();
            doodadSpawner.Id = 0;
            doodadSpawner.UnitId = id;
            doodadSpawner.Position = Connection.ActiveChar.Position.Clone();
            doodadSpawner.Position.X = x;
            doodadSpawner.Position.Y = y;
            doodadSpawner.Position.Z = z;
            doodadSpawner.Position.RotationX = 0;
            doodadSpawner.Position.RotationY = 0;
            doodadSpawner.Position.RotationZ = 0;
            doodadSpawner.Scale = scale;
            var doodad = doodadSpawner.Spawn(0, (ulong)itemId, Connection.ActiveChar.ObjId);

            if (doodad == null)
                _log.Warn("Doodad {0}, from spawn not exist at db", id);
            else
            {

                var items = ItemManager.Instance.GetItemIdsFromDoodad(id);

                if(items.Count > 0)
                {
                    var player = Connection.ActiveChar;
                    foreach (var item in items)
                    {
                        player.Inventory.ConsumeItem(null,ItemTaskType.DoodadCreate, item, 1,null);
                        /*
                        var itemToRemove = player.Inventory.GetItemByItemId(item);
                        if(itemToRemove != null)
                            InventoryHelper.RemoveItemAndUpdateClient(player, itemToRemove, 1);
                        */
                    }
                }
            }
        }
    }
}
