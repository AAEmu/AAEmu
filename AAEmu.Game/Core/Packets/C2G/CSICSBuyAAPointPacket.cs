using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Tasks.CashShop;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Requests the cash shop checkout that converts wallet cash into AA points.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value. The body is the cash to spend; the AA points it buys are
/// computed by the server at the exchange ratio the client was already told, so a tampered
/// request cannot name its own price.
/// </remarks>
public class CSICSBuyAAPointPacket() : GamePacket(CSOffsets.CSICSBuyAAPointPacket, 1)
{
    public uint CashMoney { get; private set; }

    public override void Read(PacketStream stream)
    {
        CashMoney = ReadBody(stream);

        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        // Settled off the read-only request path, the same way a goods cart is: the wallet and
        // the journal row are committed together by the task.
        TaskManager.Instance.Schedule(new CashShopAaPointPurchaseTask(CashMoney, character));
    }

    /// <summary>
    /// Reads the request body on its own. The body is a single unsigned 32-bit cash amount, the
    /// only thing the client sends; what that cash buys is decided by the server.
    /// </summary>
    public static uint ReadBody(PacketStream stream) => stream.ReadUInt32();
}

