using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTUccComplexCheckValidPacket : StreamPacket
    {
        public CTUccComplexCheckValidPacket() : base(0x12)
        {
        }

        public override void Read(PacketStream stream)
        {
            var type = stream.ReadInt16();
        }
    }
}