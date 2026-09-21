using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Crafts;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Answers the material request the board sends for one item: the item that was asked about, then the
/// materials its craft consumes.
/// </summary>
public class SCCraftOrderItemsPacket(ulong itemId, IReadOnlyList<CraftOrderMaterialRow> rows)
    : GamePacket(SCOffsets.SCCraftOrderItemsPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(itemId);
        return CraftOrderWire.WriteMaterialRows(stream, rows);
    }
}
