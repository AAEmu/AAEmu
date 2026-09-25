using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Wire contract: a single <c>uint kind</c>.
/// </summary>
/// <remarks>
/// The packet has no sender in this groundwork slice; it exists so the zone-score reset contract is
/// pinned without starting runtime score state.
/// </remarks>
public class SCZoneScoreResetPacket(uint @type) : GamePacket(SCOffsets.SCZoneScoreResetPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(@type);
        return stream;
    }
}
