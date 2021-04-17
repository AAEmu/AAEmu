using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCCharNameQueriedPacket : StreamPacket
    {
        private readonly uint _id;
        private readonly string _name;
        
        public TCCharNameQueriedPacket(uint id, string name) : base(TCOffsets.TCCharNameQueriedPacket)
        {
            _id = id;
            _name = name;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_name);

            return stream;
        }
    }
}
