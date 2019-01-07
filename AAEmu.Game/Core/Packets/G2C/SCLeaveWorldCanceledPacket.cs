using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCLeaveWorldCanceledPacket : GamePacket
    {
        public SCLeaveWorldCanceledPacket() : base(0x004, 1)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            return stream;
        }
    }
}