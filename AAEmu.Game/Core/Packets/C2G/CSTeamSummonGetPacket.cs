using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Opens a team-summon consent round for the caller's raid; the request has no body.
/// </summary>
public class CSTeamSummonGetPacket() : GamePacket(CSOffsets.CSTeamSummonGetPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        if (Connection?.ActiveChar is { } character)
            TeamJointManager.Instance.RequestSummons(character.Id);
    }
}
