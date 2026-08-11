using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitsRemovedPacket(uint[] ids) : GamePacket(SCOffsets.SCUnitsRemovedPacket, 1)
{
    public const int MaxCountPerPacket = 500; // Suggested Maximum Size (originally 300)

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ushort)ids.Length);
        foreach (var id in ids)
            stream.WriteBc(id);

        return stream;
    }

    public override string Verbose()
    {
        if (ids == null || ids.Length == 0)
            return " - Removed 0 objects";
        if (ids.Length == 1)
            return $" - Removed bc={ids[0]}";
        if (ids.Length <= 8)
            return $" - Removed {ids.Length}: [{string.Join(',', ids)}]";
        return $" - Removed {ids.Length} objects (first={ids[0]} last={ids[^1]})";
    }
}
