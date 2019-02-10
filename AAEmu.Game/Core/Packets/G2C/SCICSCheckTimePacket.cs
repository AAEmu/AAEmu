using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCICSCheckTimePacket : GamePacket
    {
        public SCICSCheckTimePacket() : base(0x1c8, 1)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            return stream;
        }
    }
}
