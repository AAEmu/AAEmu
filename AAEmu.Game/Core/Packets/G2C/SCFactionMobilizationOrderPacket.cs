using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Nation-wide broadcast that a Hero issued a Mobilization Order. The client reads the rally flag's
/// zone group as u16, then the Hero's character id as u64, then the Hero's name. Members echo those
/// two ids back on confirm — a wider first field shifts the hero id and accept never matches.
/// </summary>
public class SCFactionMobilizationOrderPacket(ushort zoneGroupType, ulong heroId, string heroName) : GamePacket(SCOffsets.SCFactionMobilizationOrderPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(zoneGroupType);
        stream.Write(heroId);
        stream.Write(heroName);
        return stream;
    }
}
