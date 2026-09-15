using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The resident zone groups of one character, sent for the Nuon's-Arrow window's zone list.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value: a total, the row count, a final-page flag, then one row per
/// zone group. Rows carry the resident point and the two money amounts, neither of which the
/// server models yet, so they are sent as zero.
/// </remarks>
public class SCResidentInfoListPacket(uint total, IReadOnlyList<ResidentInfoRow> rows)
    : GamePacket(SCOffsets.SCResidentInfoListPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(total);
        stream.Write((uint)(rows?.Count ?? 0));
        stream.Write(true); // final page: the whole list fits one packet

        if (rows == null)
            return stream;

        foreach (var row in rows)
        {
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
public readonly record struct ResidentInfoRow(
    ushort ZoneGroup,
    int Point,
    ulong MoneyAmount,
    ulong ZoneMoneyAmount,
    uint Unnamed1 = 0,
    uint Unnamed2 = 0,
    uint Unnamed3 = 0);
