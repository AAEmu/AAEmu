using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSDismissTeamPacket() : GamePacket(CSOffsets.CSDismissTeamPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var teamId = stream.ReadInt32();

        TeamManager.Instance.DismissTeam(Connection.ActiveChar, teamId);
    }
}
