using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDoodadChangedPacket : GamePacket
    {
        private readonly uint _id;
        private readonly int _data;

        public SCDoodadChangedPacket(uint id, int data) : base(SCOffsets.SCDoodadChangedPacket, 1)
        {
            _id = id;
            _data = data;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_id);
            stream.Write(_data);
            return stream;
        }
    }
}
