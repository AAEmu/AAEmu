using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFactionImmigrateInvitePacket : GamePacket
    {
        private readonly uint _id;
        private readonly uint _id2;
        
        public SCFactionImmigrateInvitePacket(uint id, uint id2) : base(SCOffsets.SCFactionImmigrateInvitePacket, 5)
        {
            _id = id;
            _id2 = id2;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_id2);
            return stream;
        }
    }
}
