using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Announces one craft the player can now use, raised after a recipe item is consumed.
/// </summary>
/// <remarks>
/// Both fields are u32, not the two sbytes this used to write. Field widths and the second field's name
/// come from the 10.0.2.13 client's serializer for the single-entry form
/// (<c>PacketLayouts/bulk/layouts-v4.tsv</c>: <c>SCCraftItemUnlockPacket 39c5cf20 0 0 0x90 4</c> and
/// <c>0 1 0x90 4 index</c>). Writing four bytes for an eight-byte body left the client reading the
/// following packet's bytes as the index.
/// </remarks>
public class SCCraftItemUnlockPacket(uint craftId, uint index) : GamePacket(SCOffsets.SCCraftItemUnlockPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(craftId);
        stream.Write(index);
        return stream;
    }
}
