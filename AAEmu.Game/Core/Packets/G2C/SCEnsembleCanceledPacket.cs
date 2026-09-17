using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Closes an ensemble, whether the leader gave up on it or a member left. It carries nothing at all: the
/// client tears down whatever ensemble it is holding.
/// </summary>
public class SCEnsembleCanceledPacket() : GamePacket(SCOffsets.SCEnsembleCanceledPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        return stream;
    }
}
