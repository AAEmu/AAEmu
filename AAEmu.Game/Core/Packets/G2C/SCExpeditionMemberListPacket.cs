using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCExpeditionMemberListPacket : GamePacket
    {
        private readonly uint _total;
        private readonly Member[] _members;

        public SCExpeditionMemberListPacket(uint total, Member[] members) : base(0x013, 1)
        {
            _total = total;
            _members = members;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_total);
            stream.Write((byte)_members.Length); // TODO max length 20
            foreach (var member in _members)
                stream.Write(member);
            return stream;
        }
    }
}
