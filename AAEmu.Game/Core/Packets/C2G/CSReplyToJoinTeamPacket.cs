using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSReplyToJoinTeamPacket : GamePacket
    {
        public CSReplyToJoinTeamPacket() : base(CSOffsets.CSReplyToJoinTeamPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var teamId = stream.ReadUInt32();
            var isParty = stream.ReadBoolean();
            var ownerId = stream.ReadUInt32();
            var isReject = stream.ReadBoolean();
            var charName = stream.ReadString();
            var isArea = stream.ReadBoolean();

            // _log.Warn("ReplyToJoinTeam, TeamId: {0}, Party: {1}, CharName: {2}, unkId: {3}, isReject: {4}, isArea: {5}", teamId, isParty, charName, ownerId, isReject, isArea);
            TeamManager.Instance.ReplyToJoinTeam(Connection.ActiveChar, teamId, isParty, ownerId, isReject, charName, isArea);
        }
    }
}
