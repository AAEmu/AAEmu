using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFactionRenamedPacket : GamePacket
    {
        private readonly uint _id;
        private readonly string _name;
        private readonly bool _byGm;
        
        public SCFactionRenamedPacket(uint id, string name, bool byGm) : base(SCOffsets.SCFactionRenamedPacket, 5)
        {
            _id = id;
            _name = name;
            _byGm = byGm;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_name);
            stream.Write(_byGm);
            return stream;
        }
    }
}
