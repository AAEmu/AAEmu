using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSMoveTeamMemberPacket : GamePacket
    {
        public CSMoveTeamMemberPacket() : base(0x081, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // TODO find unk (id, id2)
            var teamId = stream.ReadUInt32();
            var id = stream.ReadUInt32();
            var id2 = stream.ReadUInt32();
            var memberIndex = stream.ReadByte();
            var otherIndex = stream.ReadByte();
            
            _log.Warn("MoveTeamMember, TeamId: {0}, Id: {1}, {2}, Index: {3}, {4}", teamId, id, id2, memberIndex, otherIndex);
        }
    }
}
