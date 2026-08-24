using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSetPingPosPacket() : GamePacket(CSOffsets.CSSetPingPosPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var (teamId, setPingType, hasPing, position, insId) = TeamPingPosWire.Read(stream);

        var owner = Connection.ActiveChar;
        owner.LocalPingPosition = position;
        if (teamId > 0)
            TeamManager.Instance.SetPingPos(owner, teamId, hasPing, position, insId);
        else
            owner.SendPacket(new SCTeamPingPosPacket(0, hasPing, position, insId, setPingType));
    }
}
