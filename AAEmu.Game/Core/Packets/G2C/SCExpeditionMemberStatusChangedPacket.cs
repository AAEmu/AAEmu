using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCExpeditionMemberStatusChangedPacket : GamePacket
    {
        private readonly ExpeditionMember _expeditionMember;
        private readonly byte _flag;

        public SCExpeditionMemberStatusChangedPacket(ExpeditionMember expeditionMember, byte flag) : base(SCOffsets.SCExpeditionMemberStatusChangedPacket, 1)
        {
            _expeditionMember = expeditionMember;
            _flag = flag;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_expeditionMember);
            stream.Write(_flag);
            return stream;
        }
    }
}
