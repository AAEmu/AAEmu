using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// 0x24F SCICSBuySucceeded. Wire layout recovered from the 10.0.2.13 client (x2game.dll reader
/// FUN_39aa29b0): <c>u8 buyMode; u8 receiveWay; wstring receiverName; i32 chargeAaPoint;
/// i32 buyItem[10]; u8 remainBuyCount[10]</c>. The two item arrays are FIXED length 10 (no count
/// prefix): <c>buyItem[i]</c> = the purchased good's cashShopId, <c>remainBuyCount[i]</c> = its detail
/// index (must NOT be 0xFF for a used slot; empty slots are 0 and the client skips them because
/// buyItem==0). Receiving this drives the client finalize (UI event 0x27F) which shows the result
/// window and CLEARS the "loading"/waiting overlay.
/// </summary>
public class SCICSBuySucceededPacket : GamePacket
{
    /// <summary>ICS_GRW_CHARGED_MAIL — items are delivered through CommercialMail. 0 also clears loading.</summary>
    public const byte ReceiveWayChargedMail = 1;

    private const int SlotCount = 10;

    private readonly byte _buyMode;
    private readonly byte _receiveWay;
    private readonly string _receiverName;
    private readonly int _chargeAaPoint;
    private readonly IReadOnlyList<(uint cashShopId, byte detailIndex)> _items;

    public SCICSBuySucceededPacket(byte buyMode, byte receiveWay, string receiverName, int chargeAaPoint,
        IReadOnlyList<(uint cashShopId, byte detailIndex)> items)
        : base(SCOffsets.SCICSBuySucceededPacket, 1)
    {
        _buyMode = buyMode;
        _receiveWay = receiveWay;
        _receiverName = receiverName ?? "";
        _chargeAaPoint = chargeAaPoint;
        _items = items ?? [];
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_buyMode);
        stream.Write(_receiveWay);
        stream.Write(_receiverName);   // wstring [u16 byteLen][UTF-8]; empty => 0x0000
        stream.Write(_chargeAaPoint);

        for (var i = 0; i < SlotCount; i++)
            stream.Write(i < _items.Count ? (int)_items[i].cashShopId : 0);   // buyItem[10] (i32)
        for (var i = 0; i < SlotCount; i++)
            stream.Write(i < _items.Count ? _items[i].detailIndex : (byte)0); // remainBuyCount[10] (u8)

        return stream;
    }
}
