using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFactionImmigrateInviteResultPacket : GamePacket
    {
        private readonly uint _id;
        private readonly string _charName;
        private readonly bool _answer;

        public SCFactionImmigrateInviteResultPacket(uint id, string charName, bool answer) : base(SCOffsets.SCFactionImmigrateInviteResultPacket, 5)
        {
            _id = id;
            _charName = charName;
            _answer = answer;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_charName);
            stream.Write(_answer);
            return stream;
        }
    }
}
