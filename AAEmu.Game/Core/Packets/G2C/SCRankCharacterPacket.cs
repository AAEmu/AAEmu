using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCRankCharacterPacket : GamePacket
    {

        public SCRankCharacterPacket() : base(SCOffsets.SCRankCharacterPacket, 1)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            return stream;
        }
    }
}
