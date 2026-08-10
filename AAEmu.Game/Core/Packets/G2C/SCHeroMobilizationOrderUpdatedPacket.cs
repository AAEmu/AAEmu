using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// A hero's mobilization order counters: how many they have issued today, and how many this term.
/// </summary>
/// <remarks>
/// Layout from the client's reader at .text 0xaa4160 and its payload reader at 0xa3cb80, which passes
/// each value's name alongside it:
///
///   flag         +0x90  u8    read before the payload
///   type         +0x80  i32   unnamed in the stream
///   type         +0x98  u64   the hero it describes
///   todayCount   +0x80  i32   issued today, against a daily cap of 5
///   totalCount   +0x80  i32   issued this term - the "n/50" on the Mission Status tab
///
/// Sending this is what makes the issuance doodad usable. The client will not let a hero press the
/// button unless todayCount is below todayCountMax (issuance_of_mobilization_order.lua:20), and with
/// nothing ever sent it compares uninitialised values, decides the daily budget is spent - "Used all
/// your Mobilization Orders for the day (5 times)" - and disables the button, so no request can be made
/// at all. Nothing is wrong with the doodad or the interaction; the counter simply never arrived.
///
/// The first two fields are inferred rather than observed. The u8 is read on its own before the payload
/// and nothing downstream of the reader distinguishes its values, and the leading i32 sits where the
/// other hero packets carry the season. Both are sent as the obvious thing and neither has a visible
/// effect to check against, so treat them as unconfirmed if this ever misbehaves.
/// </remarks>
public class SCHeroMobilizationOrderUpdatedPacket(ulong characterId, int season, int todayCount, int totalCount)
    : GamePacket(SCOffsets.SCHeroMobilizationOrderUpdatedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)0);
        stream.Write(season);
        stream.Write(characterId);
        stream.Write(todayCount);
        stream.Write(totalCount);
        return stream;
    }
}
