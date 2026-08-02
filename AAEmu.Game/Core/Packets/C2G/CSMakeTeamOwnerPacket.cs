using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSMakeTeamOwnerPacket() : GamePacket(CSOffsets.CSMakeTeamOwnerPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var teamId = stream.ReadInt32();
        var memberId = stream.ReadUInt64();

        TeamManager.Instance.MakeTeamOwner(Connection.ActiveChar, teamId, memberId);
    }
}
