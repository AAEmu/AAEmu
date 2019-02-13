using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFactionRenamedPacket : GamePacket
    {
        private readonly uint _id;
        private readonly string _name;
        
        public SCFactionRenamedPacket(uint id, string name, bool byGm) : base(0x010, 1) // TODO 1.0 opcode: 0x00e
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
