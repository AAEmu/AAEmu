using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Tells a player that somebody is asking them to join an ensemble. The body is only the suggester, so the
/// client shows the invitation from the unit it already knows.
/// </summary>
public class SCEnsembleSuggestedPacket(uint suggesterBc) : GamePacket(SCOffsets.SCEnsembleSuggestedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(suggesterBc);
        return stream;
    }
}
