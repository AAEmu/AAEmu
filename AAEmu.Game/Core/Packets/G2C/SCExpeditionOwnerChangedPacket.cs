using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCExpeditionOwnerChangedPacket : GamePacket
    {
        private readonly uint _id;
        private readonly uint _id2;
        private readonly string _charName;

        public SCExpeditionOwnerChangedPacket(uint id, uint id2, string charName) : base(SCOffsets.SCExpeditionOwnerChangedPacket, 5)
        {
            _id = id;
            _id2 = id2;
            _charName = charName;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_id2);
            stream.Write(_charName);
            return stream;
        }
    }
}
