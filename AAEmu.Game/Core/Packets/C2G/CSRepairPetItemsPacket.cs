using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRepairPetItemsPacket : GamePacket
    {
        public CSRepairPetItemsPacket() : base(CSOffsets.CSRepairPetItemsPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var npcId = stream.ReadBc();
            
            _log.Warn("RepairPetItems, NpcId: {0}", npcId);
        }
    }
}
