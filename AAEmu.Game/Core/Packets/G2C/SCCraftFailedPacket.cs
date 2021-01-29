using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Error;

namespace AAEmu.Game.Core.Packets.G2C
{
    class SCCraftFailedPacket : GamePacket
    {

        public SCCraftFailedPacket() : base(SCOffsets.SCCraftFailedPacket, 1)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            return stream;
        }
    }
}
