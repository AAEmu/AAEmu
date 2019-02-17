using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSInviteToTeamPacket : GamePacket
    {
        public CSInviteToTeamPacket() : base(0x07a, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var teamId = stream.ReadUInt32();
            var isParty = stream.ReadBoolean();
            var characterName = stream.ReadString();

            _log.Warn("CSInviteToTeam, TeamId: {0}, IsParty: {1}, Char: {2}", teamId, isParty, characterName);
        }
    }
}
