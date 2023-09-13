using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFactionImmigrationInviteReplyPacket : GamePacket
    {
        public CSFactionImmigrationInviteReplyPacket() : base(CSOffsets.CSFactionImmigrationInviteReplyPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // TODO : check unk
            var unkId = stream.ReadUInt32();
            var unk2Id = stream.ReadUInt32();
            var answer = stream.ReadBoolean();

            _log.Debug("FactionImmigrationInviteReply, Id: {0}, Id2: {1}, Answer: {2}", unkId, unk2Id, answer);
        }
    }
}
