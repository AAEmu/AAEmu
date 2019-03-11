using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCExpeditionMemberStatusChangedPacket : GamePacket
    {
        private readonly Member _member;
        private readonly byte _flag;

        public SCExpeditionMemberStatusChangedPacket(Member member, byte flag) : base(SCOffsets.SCExpeditionMemberStatusChangedPacket, 1)
        {
            _member = member;
            _flag = flag;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_member);
            stream.Write(_flag);
            return stream;
        }
    }
}
