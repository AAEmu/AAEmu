using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCLeaveWorldCanceledPacket : GamePacket
    {
        public SCLeaveWorldCanceledPacket() : base(SCOffsets.SCLeaveWorldCanceledPacket, 5)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            return stream;
        }
    }
}
