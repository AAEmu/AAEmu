using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFactionDeclareHostileResultPacket : GamePacket
    {
        private readonly bool _result;
        private readonly uint _id;
        private readonly uint _id2;
        
        public SCFactionDeclareHostileResultPacket(bool result, uint id, uint id2) : base(SCOffsets.SCFactionDeclareHostileResultPacket, 5)
        {
            _result = result;
            _id = id;
            _id2 = id2;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_result);
            stream.Write(_id);
            stream.Write(_id2);
            return stream;
        }
    }
}
