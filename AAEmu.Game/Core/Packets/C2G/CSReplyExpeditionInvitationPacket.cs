using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSReplyExpeditionInvitationPacket : GamePacket
    {
        public CSReplyExpeditionInvitationPacket() : base(0x00d, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // TODO: Check what those IDs are.
            // My guess is that one of them is expedition id, the other is invited character id.
            var unkId = stream.ReadUInt32(); // type(id)
            var unk2Id = stream.ReadUInt32(); // type(id)
            var join = stream.ReadBoolean();

            _log.Debug("ReplyExpeditionInvitation, Id: {0}, Id2: {1}, Join: {2}", unkId, unk2Id, join);
        }
    }
}
