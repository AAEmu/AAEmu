using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The resident zone groups of one character, sent for the Nuon's-Arrow window's zone list.
/// </summary>
/// <remarks>
/// Opcode, field order, widths and names all come from the 10.0.2.13 client. The client's own
/// constructor for this type is what stamps the opcode, and its serializer names each value as it
/// writes it: u32 <c>total</c>, u32 <c>count</c>, bool <c>final</c>, then <c>count</c> rows of i16
/// <c>type</c>, u32 <c>point</c>, u64 <c>moneyAmount</c>, u64 <c>moneyAmount</c>, then three i32s.
/// The point and the two money amounts are not modelled server-side, so they go out as zero.
/// </remarks>
public class SCResidentInfoListPacket(uint total, IReadOnlyList<ResidentInfoRow> rows)
    : GamePacket(SCOffsets.SCResidentInfoListPacket, 1)
{
    /// <summary>
    /// The client reads at most this many rows and sizes its row array to match: it compares
    /// <c>count</c> against 100 and zeroes 100 rows of 0x28 bytes each. A longer list would
    /// leave rows in the stream for the next packet in the batch to read.
    /// </summary>
    private const int MaxRows = 100;

    public override PacketStream Write(PacketStream stream)
    {
        var count = Math.Min(rows?.Count ?? 0, MaxRows);

        stream.Write(total);
        stream.Write((uint)count);
        stream.Write(true); // final page: the whole list fits one packet

        for (var i = 0; i < count; i++)
        {
            var row = rows![i];
            stream.Write(row.ZoneGroup);
            stream.Write(row.Point);
            stream.Write(row.MoneyAmount);
            stream.Write(row.ZoneMoneyAmount);
            stream.Write(row.Unnamed1);
            stream.Write(row.Unnamed2);
            stream.Write(row.Unnamed3);
        }

        return stream;
    }
}

/// <summary>One resident zone group row of <see cref="SCResidentInfoListPacket"/>.</summary>
/// <remarks>
/// Widths and order are the client row serializer's: i16, u32, u64, u64, i32, i32, i32. The client
/// names the first field <c>type</c>; the last three carry the same <c>type</c> literal and are left
/// at zero here, because nothing models them.
/// </remarks>
public readonly record struct ResidentInfoRow(
    ushort ZoneGroup,
    uint Point,
    ulong MoneyAmount,
    ulong ZoneMoneyAmount,
    int Unnamed1 = 0,
    int Unnamed2 = 0,
    int Unnamed3 = 0);
