using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// </remarks>
public class CSTeamHandOverOwnerResponsePacket() : GamePacket(CSOffsets.CSTeamHandOverOwnerResponsePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var teamId = stream.ReadInt32();
        var ownerId = stream.ReadUInt64();
        var candidateId = stream.ReadUInt64();
        var reason = stream.ReadSByte();
        var accept = stream.ReadBoolean();

        TeamManager.Instance.RespondToOwnerHandover(
            Connection.ActiveChar, teamId, ownerId, candidateId, reason, accept, true);
    }
}
