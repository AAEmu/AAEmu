using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSInviteAreaToTeamPacket() : GamePacket(CSOffsets.CSInviteAreaToTeamPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // i32 tid, bool isParty.
        var teamId = stream.ReadInt32();
        var isParty = stream.ReadBoolean();

        TeamManager.Instance.InviteAreaToTeam(Connection.ActiveChar, teamId, isParty);
    }
}
