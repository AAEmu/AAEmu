using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFactionOwnerChangedPacket : GamePacket
    {
        private readonly uint _id;
        private readonly uint _id2;
        private readonly string _newOwnerName;
        
        public SCFactionOwnerChangedPacket(uint id, uint id2, string newOwnerName) : base(SCOffsets.SCFactionOwnerChangedPacket, 5)
        {
            _id = id;
            _id2 = id2;
            _newOwnerName = newOwnerName;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_id2);
            stream.Write(_newOwnerName);
            return stream;
        }
    }
}
