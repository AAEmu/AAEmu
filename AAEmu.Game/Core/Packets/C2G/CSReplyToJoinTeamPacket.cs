using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSReplyToJoinTeamPacket : GamePacket
    {
        public CSReplyToJoinTeamPacket() : base(0x07c, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var teamId = stream.ReadUInt32();
            var party = stream.ReadBoolean();
            var unkId = stream.ReadUInt32(); // TODO Owner?
            var isReject = stream.ReadBoolean();
            var charName = stream.ReadString();
            var isArea = stream.ReadBoolean();

            _log.Warn("ReplyToJoinTeam, TeamId: {0}, Party: {1}, CharName: {2}", teamId, party, charName);
        }
    }
}
