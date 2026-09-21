using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Asks which materials one item consumes. The restore tab sends a sheet instance id; the post
/// dialog sends the product type of the craft being posted.
/// </summary>
public class CSRequestCraftOrderItemsPacket() : GamePacket(CSOffsets.CSRequestCraftOrderItemsPacket, 1)
{
    public long ItemId { get; private set; }

    public override void Read(PacketStream stream)
    {
        ItemId = stream.ReadInt64();

        if (Connection?.ActiveChar is { } character)
            CraftOrderManager.Instance.SendMaterials(character, (ulong)Math.Max(0, ItemId));
    }
}
