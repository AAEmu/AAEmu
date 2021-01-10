using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFactionImmigrateToOriginResultPacket : GamePacket
    {
        private readonly string _charName;
        private readonly uint _id;
        
        public SCFactionImmigrateToOriginResultPacket(string charName, uint id) : base(SCOffsets.SCFactionImmigrateToOriginResultPacket, 5)
        {
            _charName = charName;
            _id = id;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_charName);
            stream.Write(_id);
            return stream;
        }
    }
}
