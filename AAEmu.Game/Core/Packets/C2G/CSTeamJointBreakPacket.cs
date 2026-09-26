using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Requests or answers the joint break handshake between two federated raid teams.
/// </summary>
/// <remarks>
/// Body: bool ask, bool accept.
/// </remarks>
public class CSTeamJointBreakPacket() : GamePacket(CSOffsets.CSTeamJointBreakPacket, 1)
{
    public bool Ask { get; private set; }
    public bool Accept { get; private set; }

    public override void Read(PacketStream stream)
    {
        Ask = stream.ReadBoolean();
        Accept = stream.ReadBoolean();

        if (Connection?.ActiveChar is { } character)
            TeamJointManager.Instance.RespondToJointBreak(character.Id, Ask, Accept);
    }
}
