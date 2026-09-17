using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Tells the leader that a player turned the ensemble down, so the invitation can be taken off the table.
/// </summary>
public class SCEnsembleRejectPacket(uint rejecterBc) : GamePacket(SCOffsets.SCEnsembleRejectPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(rejecterBc);
        return stream;
    }
}
