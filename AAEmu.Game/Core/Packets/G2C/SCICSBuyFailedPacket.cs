using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// 0x250 SCICSBuyFailed. Wire layout recovered from the 10.0.2.13 client (x2game.dll reader
/// FUN_39ab9510): <c>u8 buyMode; u16 reason; i32 buyItem[10]; u16 eachReason[10]</c>. The client only
/// reads buyItem/eachReason when <c>reason == 0x4F8</c> (per-item mixed failure); a simple failure
/// sends them as zeros. <c>reason</c> is a client BuyFailReason wire code — it must NOT be 0 or 0x13,
/// which the client ignores (leaving the shop stuck on "loading"). A code that maps to a real
/// BuyFailReason routes through the finalize (UI event 0x27F) that clears the loading overlay.
/// </summary>
public class SCICSBuyFailedPacket(byte buyMode, ushort reason) : GamePacket(SCOffsets.SCICSBuyFailedPacket, 1)
{
    /// <summary>Generic error toast that reliably clears the loading state (maps to a real BuyFailReason).</summary>
    public const ushort ReasonGeneric = 0x024F;

    /// <summary>"Not enough cash" — opens the client's charge dialog (event 0x2AF) instead of a toast.</summary>
    public const ushort ReasonNotEnoughCash = 0x030A;

    private const int SlotCount = 10;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(buyMode);
        stream.Write(reason);
        for (var i = 0; i < SlotCount; i++) stream.Write(0);          // buyItem[10] (i32) — only read when reason==0x4F8
        for (var i = 0; i < SlotCount; i++) stream.Write((ushort)0);  // eachReason[10] (u16) — idem
        return stream;
    }
}
