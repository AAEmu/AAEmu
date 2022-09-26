using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSPacketUnknown0x166Packet : GamePacket
    {
        public CSPacketUnknown0x166Packet() : base(CSOffsets.CSPacketUnknown0x166Packet, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            //var open = stream.ReadByte();

            _log.Warn("CSPacketUnknown0x166Packet");
        }
    }
}
