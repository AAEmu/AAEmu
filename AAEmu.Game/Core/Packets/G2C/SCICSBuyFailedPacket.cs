using AAEmu.Commons.Network;

using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Reports a failed Marketplace purchase.</summary>
public sealed class SCICSBuyFailedPacket : GamePacket
{
    private const int SlotCount = 10;

    private readonly byte _buyMode;
    private readonly short _reason;
    private readonly IReadOnlyList<(uint CashShopId, ErrorMessageType Reason)> _itemFailures;

    public SCICSBuyFailedPacket(
        byte buyMode,
        ErrorMessageType reason,
        IReadOnlyList<(uint CashShopId, ErrorMessageType Reason)> itemFailures = null)
        : base(SCOffsets.SCICSBuyFailedPacket, 1)
    {
        _buyMode = buyMode == 0 ? (byte)1 : buyMode;
        _reason = (short)reason;
        _itemFailures = itemFailures ?? [];
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_buyMode);
        stream.Write(_reason);

        for (var i = 0; i < SlotCount; i++)
            stream.Write(i < _itemFailures.Count ? _itemFailures[i].CashShopId : 0u);
        for (var i = 0; i < SlotCount; i++)
            stream.Write(i < _itemFailures.Count ? (short)_itemFailures[i].Reason : (short)0);

        return stream;
    }
}
