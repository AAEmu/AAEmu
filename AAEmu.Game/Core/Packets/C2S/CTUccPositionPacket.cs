using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTUccPositionPacket : StreamPacket
    {
        public CTUccPositionPacket() : base(0x08)
        {

        }

        public override void Read(PacketStream stream)
        {
            var type = stream.ReadInt64();
        }
    }
}