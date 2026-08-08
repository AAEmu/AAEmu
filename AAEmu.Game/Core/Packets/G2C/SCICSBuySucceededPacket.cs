using AAEmu.Commons.Network;

using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Reports a successful Marketplace purchase.</summary>
public sealed class SCICSBuySucceededPacket : GamePacket
{
    public const byte ReceiveWayChargedMail = 1;

    private const int SlotCount = 10;

    private readonly byte _buyMode;
    private readonly byte _receiveWay;
    private readonly string _receiverName;
    private readonly int _chargeAaPoint;
    private readonly IReadOnlyList<(uint CashShopId, byte DetailIndex)> _items;

    public SCICSBuySucceededPacket(
        byte buyMode,
        byte receiveWay,
        string receiverName,
        int chargeAaPoint,
        IReadOnlyList<(uint CashShopId, byte DetailIndex)> items)
        : base(SCOffsets.SCICSBuySucceededPacket, 1)
    {
        _buyMode = buyMode == 0 ? (byte)1 : buyMode;
        _receiveWay = receiveWay;
        _receiverName = receiverName ?? string.Empty;
        _chargeAaPoint = chargeAaPoint;
        _items = items ?? [];
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_buyMode);
        stream.Write(_receiveWay);
        stream.Write(_receiverName);
        stream.Write(_chargeAaPoint);

        for (var i = 0; i < SlotCount; i++)
            stream.Write(i < _items.Count ? _items[i].CashShopId : 0u);
        for (var i = 0; i < SlotCount; i++)
            stream.Write(i < _items.Count ? _items[i].DetailIndex : (byte)0);

        return stream;
    }
}
