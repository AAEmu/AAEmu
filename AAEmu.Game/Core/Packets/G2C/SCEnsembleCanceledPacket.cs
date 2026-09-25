using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Closes an ensemble when its maestro leaves or a disconnected performance is cleaned up. A member
/// leaving only drops that member's part. It carries nothing: the client tears down its current ensemble.
/// </summary>
public class SCEnsembleCanceledPacket() : GamePacket(SCOffsets.SCEnsembleCanceledPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        return stream;
    }
}
