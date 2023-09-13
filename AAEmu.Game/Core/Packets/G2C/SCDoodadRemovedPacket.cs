using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDoodadRemovedPacket : GamePacket
    {
        private readonly uint _id;

        public SCDoodadRemovedPacket(uint id) : base(SCOffsets.SCDoodadRemovedPacket, 1)
        {
            _id = id;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_id);
            stream.Write(false); // e if false then the doodad will be deleted
            return stream;
        }
    }
}
