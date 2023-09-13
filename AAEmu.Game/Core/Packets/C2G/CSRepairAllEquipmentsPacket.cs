using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRepairAllEquipmentsPacket : GamePacket
    {
        public CSRepairAllEquipmentsPacket() : base(CSOffsets.CSRepairAllEquipmentsPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var autoUseAAPoint = stream.ReadBoolean();

            _log.Debug("RepairAllEquipments, AutoUseAAPoint: {0}", autoUseAAPoint);

            var items = new List<Item>();
            foreach (var item in Connection.ActiveChar.Inventory.Equipment.Items)
            {
                items.Add(item);
            }

            Connection.ActiveChar.DoRepair(items);
        }
    }
}
