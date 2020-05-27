using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCIdleKickPacket : GamePacket
    {
        public SCIdleKickPacket() : base(SCOffsets.SCIdleKickPacket, 1)
        {

        }

        public override PacketStream Write(PacketStream stream)
        {
            return stream;
        }
    }
}
