using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.TowerDefs;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Live tower-def set for world-map marks (icon_key from tower_defs, e.g. Crimson "sign" skull).
/// Client replaces its full active list when this arrives.
/// </summary>
public sealed class SCTowerDefActiveInfoListPacket(IReadOnlyList<TowerDefActiveInfo> entries)
    : GamePacket(SCOffsets.SCTowerDefActiveInfoListPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        var list = entries ?? Array.Empty<TowerDefActiveInfo>();
        // Client "Size" field is a 32-bit count (unlike SCTowerDefListPacket's u8).
        stream.Write(list.Count);
        foreach (var entry in list)
            entry.Write(stream);
        return stream;
    }
}
