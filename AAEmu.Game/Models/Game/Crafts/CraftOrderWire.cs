using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Crafts;

/// <summary>
/// One craft order as the client stores it: the order the character posted, and the order a search
/// result describes.
/// </summary>
/// <param name="Id">Order id. The cancel and delete packets carry this same value.</param>
/// <param name="Kind">Order kind, in the client's own id space.</param>
/// <param name="Unnamed1">
/// A field the client reads without a name of its own, so only its width and position are fixed.
/// Shape suggests a character key of the same form the client uses elsewhere
/// (<c>characterId | worldId &lt;&lt; 32</c>); name it from a live read before relying on that.
/// </param>
/// <param name="OrderItemId">Item the order asks for.</param>
/// <param name="Unnamed2">Unnamed u32; see <paramref name="Unnamed1"/>.</param>
/// <param name="CraftCount">How many the order asks for.</param>
/// <param name="Unnamed3">Unnamed byte; see <paramref name="Unnamed1"/>.</param>
/// <param name="MoneyAmount">Fee offered for the order, in copper.</param>
/// <param name="Unnamed4">Unnamed u32; see <paramref name="Unnamed1"/>.</param>
/// <param name="ActabilityPoint">Actability the fulfiller needs.</param>
/// <param name="PostDate">When the order was posted. Epoch unit not established.</param>
/// <param name="ExpireDate">When the order lapses. Same unit as <paramref name="PostDate"/>.</param>
/// <param name="Status">Order status, in the client's own id space.</param>
/// <param name="Unnamed5">Unnamed u64; see <paramref name="Unnamed1"/>.</param>
public readonly record struct CraftOrderEntry(
    ulong Id,
    byte Kind,
    ulong Unnamed1,
    ulong OrderItemId,
    uint Unnamed2,
    uint CraftCount,
    byte Unnamed3,
    ulong MoneyAmount,
    uint Unnamed4,
    uint ActabilityPoint,
    ulong PostDate,
    ulong ExpireDate,
    byte Status,
    ulong Unnamed5);

/// <summary>
/// One row of the material list a craft order needs before it can be filled.
/// </summary>
/// <param name="Unnamed1">The client reads this as a type; width is all that is fixed.</param>
/// <param name="Unnamed2">The client reads this as a type too; width is all that is fixed.</param>
/// <param name="Stack">How many of the material the order consumes.</param>
public readonly record struct CraftOrderMaterialRow(uint Unnamed1, byte Unnamed2, uint Stack);

/// <summary>
/// The craft-order wire shared by the packets that carry entries.
///
/// The client keeps at most <see cref="LoadEntryLimit"/> entries per character, accepts at most
/// <see cref="SearchEntryLimit"/> on one search page and at most <see cref="MaterialRowLimit"/>
/// material rows for one item; it substitutes those caps for whatever count it is sent, so a longer
/// list is not sent truncated — it is refused here instead.
/// </summary>
public static class CraftOrderWire
{
    /// <summary>Entries the client keeps for one character.</summary>
    public const int LoadEntryLimit = 5;

    /// <summary>Entries the client accepts on one search page.</summary>
    public const int SearchEntryLimit = 8;

    /// <summary>Material rows the client accepts for one item.</summary>
    public const int MaterialRowLimit = 12;

    /// <summary>Bytes one <see cref="CraftOrderEntry"/> occupies on the wire.</summary>
    public const int EntrySize = 75;

    /// <summary>Bytes one <see cref="CraftOrderMaterialRow"/> occupies on the wire.</summary>
    public const int MaterialRowSize = 9;

    /// <summary>Writes one entry, in the order the client reads its fields.</summary>
    public static PacketStream WriteEntry(PacketStream stream, CraftOrderEntry entry)
    {
        stream.Write(entry.Id);
        stream.Write(entry.Kind);
        stream.Write(entry.Unnamed1);
        stream.Write(entry.OrderItemId);
        stream.Write(entry.Unnamed2);
        stream.Write(entry.CraftCount);
        stream.Write(entry.Unnamed3);
        stream.Write(entry.MoneyAmount);
        stream.Write(entry.Unnamed4);
        stream.Write(entry.ActabilityPoint);
        stream.Write(entry.PostDate);
        stream.Write(entry.ExpireDate);
        stream.Write(entry.Status);
        stream.Write(entry.Unnamed5);
        return stream;
    }

    /// <summary>Writes a count-prefixed entry list, refusing more than <paramref name="limit"/>.</summary>
    public static PacketStream WriteEntries(PacketStream stream, IReadOnlyList<CraftOrderEntry> entries, int limit)
    {
        var rows = entries ?? [];
        if (rows.Count > limit)
            throw new InvalidOperationException(
                $"Craft order list has {rows.Count} entries, the client accepts {limit}");

        stream.Write((uint)rows.Count);
        foreach (var entry in rows)
            WriteEntry(stream, entry);
        return stream;
    }

    /// <summary>Writes a count-prefixed material list, refusing more than <see cref="MaterialRowLimit"/>.</summary>
    public static PacketStream WriteMaterialRows(PacketStream stream, IReadOnlyList<CraftOrderMaterialRow> rows)
    {
        var list = rows ?? [];
        if (list.Count > MaterialRowLimit)
            throw new InvalidOperationException(
                $"Craft order material list has {list.Count} rows, the client accepts {MaterialRowLimit}");

        stream.Write((uint)list.Count);
        foreach (var row in list)
        {
            stream.Write(row.Unnamed1);
            stream.Write(row.Unnamed2);
            stream.Write(row.Stack);
        }

        return stream;
    }
}
