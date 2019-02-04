using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTUccComplexPacket : StreamPacket
    {
        public CTUccComplexPacket() : base(0x06)
        {
        }

        public override void Read(PacketStream stream)
        {
            var type = stream.ReadInt64();
        }
    }
}