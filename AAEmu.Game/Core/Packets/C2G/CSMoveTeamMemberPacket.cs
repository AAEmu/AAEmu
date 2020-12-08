using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSMoveTeamMemberPacket : GamePacket
    {
        public CSMoveTeamMemberPacket() : base(CSOffsets.CSMoveTeamMemberPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var teamId = stream.ReadUInt32();
            var targetId = stream.ReadUInt32();
            var target2Id = stream.ReadUInt32();
            var fromIndex = stream.ReadByte();
            var toIndex = stream.ReadByte();

            // _log.Warn("MoveTeamMember, TeamId: {0}, Id: {1}, {2}, Index: {3}, {4}", teamId, id, id2, memberIndex, otherIndex);
            TeamManager.Instance.MoveTeamMember(Connection.ActiveChar, teamId, targetId, target2Id, fromIndex, toIndex);
        }
    }
}
