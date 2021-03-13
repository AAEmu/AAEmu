using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSBroadcastOpenEquipInfoPacket : GamePacket
    {
        public CSBroadcastOpenEquipInfoPacket() : base(CSOffsets.CSBroadcastOpenEquipInfoPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var open = stream.ReadByte();

            _log.Warn("CSBroadcastOpenEquipInfoPacket, open: {0}", open);
        }
    }
}
