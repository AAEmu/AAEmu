using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSReplyInviteJuryPacket : GamePacket
    {
        public CSReplyInviteJuryPacket() : base(CSOffsets.CSReplyInviteJuryPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var accept = stream.ReadBoolean();
            var trial = stream.ReadUInt32();

            _log.Warn("ReplyInviteJury, Accept: {0}, Trial: {1}", accept, trial);
        }
    }
}
