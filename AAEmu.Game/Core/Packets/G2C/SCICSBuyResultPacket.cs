using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Reports the result of a Marketplace purchase.</summary>
public class SCICSBuyResultPacket : GamePacket
{
    private readonly bool _success;
    private readonly byte _buyMode;
    private readonly string _receiverName;
    private readonly int _chargeAaPoint;
    private readonly short _errorMessage;
    private readonly uint[] _buyItems;
    private readonly short[] _slotErrors;
    private readonly byte[] _remainBuyCount;

    public SCICSBuyResultPacket(
        bool success,
        byte buyMode,
        string receiverName,
        int chargeAaPoint,
        ErrorMessageType failError = ErrorMessageType.Invalid,
        uint[] buyItems = null,
        short[] slotErrors = null,
        byte[] remainBuyCount = null)
        : base(success ? SCOffsets.SCICSBuyResultPacket : SCOffsets.SCICSBuyFailedPacket, 1)
    {
        _success = success;
        _buyMode = buyMode;
        _receiverName = receiverName ?? string.Empty;
        _chargeAaPoint = chargeAaPoint;
        _errorMessage = (short)failError;
        _buyItems = buyItems;
        _slotErrors = slotErrors;
        _remainBuyCount = remainBuyCount;
    }

    /// <summary>Builds a failure reply with a supported error message.</summary>
    public static SCICSBuyResultPacket Fail(
        byte buyMode,
        string receiverName,
        ErrorMessageType failError,
        uint shopId = 0)
    {
        // Normalize the mode so the pending purchase can be completed on failure.
        var mode = buyMode == 0 ? (byte)1 : buyMode;
        var buyItems = new uint[10];
        var slotErrors = new short[10];
        if (shopId != 0)
        {
            buyItems[0] = shopId;
            // Per-slot ErrorMessage must also be ErrorMessageType ids (same mapper).
            slotErrors[0] = (short)failError;
        }

        return new SCICSBuyResultPacket(
            false, mode, receiverName, 0, failError, buyItems, slotErrors);
    }

    public override PacketStream Write(PacketStream stream)
    {
        if (_success)
        {
            stream.Write(_buyMode);
            stream.Write((byte)0); // receiveWay (0 = charged/marketplace mail)
            stream.Write(_receiverName);
            stream.Write(_chargeAaPoint);
            for (var i = 0; i < 10; i++)
                stream.Write(_buyItems != null && i < _buyItems.Length ? _buyItems[i] : 0u);
            for (var i = 0; i < 10; i++)
                stream.Write(_remainBuyCount != null && i < _remainBuyCount.Length ? _remainBuyCount[i] : (byte)0);
            return stream;
        }

        // SCICSBuyFailed: buyMode u8, ErrorMessage i16 (ErrorMessageType), 10× buyItem u32, 10× i16
        stream.Write(_buyMode);
        stream.Write(_errorMessage);
        for (var i = 0; i < 10; i++)
            stream.Write(_buyItems != null && i < _buyItems.Length ? _buyItems[i] : 0u);
        for (var i = 0; i < 10; i++)
            stream.Write(_slotErrors != null && i < _slotErrors.Length ? _slotErrors[i] : (short)0);
        return stream;
    }
}
