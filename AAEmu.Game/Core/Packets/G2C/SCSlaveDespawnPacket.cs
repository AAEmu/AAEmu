using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSlaveDespawnPacket : GamePacket
    {
        private readonly uint _id;

        public SCSlaveDespawnPacket(uint id) : base(SCOffsets.SCSlaveDespawnPacket, 5)
        {
            _id = id;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_id);
            return stream;
        }
    }
}
