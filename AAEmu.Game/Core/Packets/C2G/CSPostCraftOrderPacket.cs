using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Posts a craft order: the item the character wants made, and the fee they offer for it. The fee is
/// held by the board until the order is filled or cancelled.
/// </summary>
public class CSPostCraftOrderPacket() : GamePacket(CSOffsets.CSPostCraftOrderPacket, 1)
{
    public long ItemId { get; private set; }
    public ulong MoneyAmount { get; private set; }

    public override void Read(PacketStream stream)
    {
        ItemId = stream.ReadInt64();
        MoneyAmount = stream.ReadUInt64();

        if (Connection?.ActiveChar is { } character)
            CraftOrderManager.Instance.Post(character, (ulong)Math.Max(0, ItemId), MoneyAmount);
    }
}
