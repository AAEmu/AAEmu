using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSAskRiskyTeamActionPacket() : GamePacket(CSOffsets.CSAskRiskyTeamActionPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // i32 teamId, u64 type, u8 ra.
        var teamId = stream.ReadInt32();
        var targetId = stream.ReadUInt64();
        var riskyAction = (RiskyAction)stream.ReadByte();

        TeamManager.Instance.AskRiskyTeam(Connection.ActiveChar, teamId, targetId, riskyAction);
    }
}
