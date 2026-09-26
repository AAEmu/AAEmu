using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Joint break handshake result, body u8 ask + u8 accept. Sent by the joint manager both to ask the
/// other raid to break and to report the outcome.
/// </summary>
public class SCTeamJointBreakPacket(bool ask, bool accept) : GamePacket(SCOffsets.SCTeamJointBreakPacket, 1)
{
    /// <summary>True when this is the prompt asking the other raid to agree to a break.</summary>
    public bool Ask { get; } = ask;

    /// <summary>True when the answer given to the prompt was yes.</summary>
    public bool Accept { get; } = accept;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(ask);
        stream.Write(accept);
        return stream;
    }
}
