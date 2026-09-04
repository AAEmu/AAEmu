using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </summary>
/// <remarks>
/// FIXED (was flagged by review, and confirmed against the client decompile): this used to trust the
/// client-reported Addexp value directly and hand it straight to ExpeditionManager.AddExp - any guild
/// member could submit an arbitrary amount and instantly level their guild, unlocking guild-wide
/// benefits with no payment/authorization check at all.
///
/// The real client has exactly one send site for this packet (FUN_395b7930): once per local calendar
/// day (compares the guild's last-known date against "now" via XlGetLocalTime), it sends this packet
/// with Addexp hardcoded to 0 - `FUN_39a8f240(&amp;local_78, myExpeditionId, 0)`. It is a "new day,
/// please refresh" heartbeat, not an EXP submission - retail never sends a nonzero value here, so
/// accepting one at all was pure client-trusted input with no legitimate use. There is no known item/
/// consumable this packet confirms the consumption of either, and no confirmed server-side "daily guild
/// EXP" formula to grant instead, so the amount is discarded entirely below rather than guessed at.
/// TypeValue's meaning is unconfirmed - not used below.
/// </remarks>
public class CSExpeditionExpAddPacket() : GamePacket(CSOffsets.CSExpeditionExpAddPacket, 1)
{
    public int TypeValue { get; private set; }
    public uint Addexp { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadInt32();
        Addexp = stream.ReadUInt32();
        // Client-supplied amount intentionally ignored - see the FIXED remark above.
    }
}
