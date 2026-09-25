using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Wire contract: <c>ushort kind</c>, <c>uint kindId</c>, <c>signed int pointDelta</c>.
/// </summary>
/// <remarks>
/// The packet has no sender in this groundwork slice; it exists so the faction-scoring contract is
/// pinned without starting runtime score state.
/// </remarks>
public class SCFactionCompetitionUpdatePointPacket(ushort @type, uint @type2, int nUpdatePoint) : GamePacket(SCOffsets.SCFactionCompetitionUpdatePointPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(@type);
        stream.Write(@type2);
        stream.Write(nUpdatePoint);
        return stream;
    }
}
