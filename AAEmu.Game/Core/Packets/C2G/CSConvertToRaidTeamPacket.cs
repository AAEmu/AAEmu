using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSConvertToRaidTeamPacket() : GamePacket(CSOffsets.CSConvertToRaidTeamPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // i32 tid.
        var teamId = stream.ReadInt32();

        TeamManager.Instance.ConvertToRaid(Connection.ActiveChar, teamId);
    }
}
