using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTradeMadePacket : GamePacket
    {
        public SCTradeMadePacket() : base(0x168, 1) // TODO - struct
        {

        }

        public override PacketStream Write(PacketStream stream)
        {
            return stream;
        }
    }
}
