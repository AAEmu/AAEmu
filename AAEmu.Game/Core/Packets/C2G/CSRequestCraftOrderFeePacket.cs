using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Asks what an order for this craft currently costs on the board, which is what the post dialog
/// shows as its cheapest and richest offer.
/// </summary>
public class CSRequestCraftOrderFeePacket() : GamePacket(CSOffsets.CSRequestCraftOrderFeePacket, 1)
{
    public int TypeValue { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadInt32();

        if (Connection?.ActiveChar is { } character)
            CraftOrderManager.Instance.SendFeeInfo(character, (uint)Math.Max(0, TypeValue));
    }
}
