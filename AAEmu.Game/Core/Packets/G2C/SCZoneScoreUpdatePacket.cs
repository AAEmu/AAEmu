using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Wire contract: <c>uint kind</c> followed by a <c>signed int scoreDelta</c>.
/// </summary>
/// <remarks>
/// The packet has no sender in this groundwork slice; it exists so the zone-score contract is
/// pinned without starting runtime score state.
/// </remarks>
public class SCZoneScoreUpdatePacket(uint @type, int diff) : GamePacket(SCOffsets.SCZoneScoreUpdatePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(@type);
        stream.Write(diff);
        return stream;
    }
}
