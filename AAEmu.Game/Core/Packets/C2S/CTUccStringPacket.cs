using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTUccStringPacket : StreamPacket
    {
        public CTUccStringPacket() : base(0x07)
        {

        }

        public override void Read(PacketStream stream)
        {
            var type = stream.ReadInt64();
        }
    }
}