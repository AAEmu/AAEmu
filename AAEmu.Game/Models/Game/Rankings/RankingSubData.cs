using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Rankings;

/// <summary>
/// The shapes a board line's own detail block takes. The client reads the kind as a byte and then a
/// fixed payload for it, so the block's length follows from the kind rather than from a count.
/// </summary>
public enum RankingSubDataKind : byte
{
    /// <summary>No block; the flag on the line stays clear.</summary>
    None = 0,

    /// <summary>The item an item board measured: its template id, then a byte the window does not read.</summary>
    ItemId = 1,

    /// <summary>Two counts.</summary>
    TwoCounts = 2,

    /// <summary>One count.</summary>
    OneCount = 3,

    /// <summary>A battlefield record: wins, losses, draws, kills, deaths.</summary>
    BattleRecord = 4,

    /// <summary>A win record: wins, losses, draws.</summary>
    WinRecord = 5
}

/// <summary>
/// The detail a board line carries beside its two figures: the item an item board ranks, or the counts a
/// board over expeditions and battlefields shows. Which count means what is the board kind's business —
/// what the client reads is the shape, so the shape is what this keeps.
/// </summary>
/// <remarks>
/// Written after the line's own fields and before its standing, and only when the kind is not
/// <see cref="RankingSubDataKind.None"/>. The payload widths are the client's and are exact: a payload of
/// the wrong width leaves it reading the rest of the line as detail.
/// </remarks>
public sealed class RankingSubData
{
    /// <summary>The widest payload any kind carries.</summary>
    public const int MaxPayloadBytes = 20;

    private readonly int[] _counts;

    private RankingSubData(RankingSubDataKind kind, uint itemId, int[] counts)
    {
        Kind = kind;
        ItemId = itemId;
        _counts = counts;
    }

    public RankingSubDataKind Kind { get; }

    /// <summary>The item template id, for <see cref="RankingSubDataKind.ItemId"/>.</summary>
    public uint ItemId { get; }

    /// <summary>The counts, in the order the client reads them. Their meaning is the board kind's.</summary>
    public IReadOnlyList<int> Counts => _counts;

    /// <summary>The owned item a board of equipped pieces ranks.</summary>
    public static RankingSubData ForItem(uint itemTemplateId)
    {
        return new RankingSubData(RankingSubDataKind.ItemId, itemTemplateId, []);
    }

    /// <summary>One count, which a board over expeditions shows as its member count.</summary>
    public static RankingSubData ForOneCount(int count)
    {
        return new RankingSubData(RankingSubDataKind.OneCount, 0, [count]);
    }

    /// <summary>Two counts.</summary>
    public static RankingSubData ForTwoCounts(int first, int second)
    {
        return new RankingSubData(RankingSubDataKind.TwoCounts, 0, [first, second]);
    }

    /// <summary>A win record: wins, losses and draws.</summary>
    public static RankingSubData ForWinRecord(int win, int lose, int draw)
    {
        return new RankingSubData(RankingSubDataKind.WinRecord, 0, [win, lose, draw]);
    }

    /// <summary>A battlefield record: wins, losses, draws, kills and deaths.</summary>
    public static RankingSubData ForBattleRecord(int win, int lose, int draw, int kill, int death)
    {
        return new RankingSubData(RankingSubDataKind.BattleRecord, 0, [win, lose, draw, kill, death]);
    }

    /// <summary>Writes the kind byte and the payload the kind fixes.</summary>
    public void Write(PacketStream stream)
    {
        stream.Write((byte)Kind);

        switch (Kind)
        {
            case RankingSubDataKind.ItemId:
                stream.Write(ItemId);

                // The window takes five payload bytes for an item and reads only the id out of them. The
                // width is what keeps the rest of the line aligned, so the byte the window skips is
                // written rather than left out.
                stream.Write((byte)0);
                break;
            case RankingSubDataKind.OneCount:
                stream.Write(Counts[0]);
                break;
            case RankingSubDataKind.TwoCounts:
                stream.Write(Counts[0]);
                stream.Write(Counts[1]);
                break;
            case RankingSubDataKind.WinRecord:
                stream.Write(Counts[0]);
                stream.Write(Counts[1]);
                stream.Write(Counts[2]);
                break;
            case RankingSubDataKind.BattleRecord:
                stream.Write(Counts[0]);
                stream.Write(Counts[1]);
                stream.Write(Counts[2]);
                stream.Write(Counts[3]);
                stream.Write(Counts[4]);
                break;
        }
    }

    /// <summary>The block as it goes on the wire, which is also how a board keeps it between restarts.</summary>
    public byte[] ToBytes()
    {
        var stream = new PacketStream();
        Write(stream);
        return stream.GetBytes();
    }

    /// <summary>
    /// The block a stored byte run holds, or null when there is none or its kind is not one the client
    /// reads. Null is the honest answer for an unreadable block: the line is then sent without one.
    /// </summary>
    public static RankingSubData FromBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return null;

        return (RankingSubDataKind)bytes[0] switch
        {
            RankingSubDataKind.ItemId when bytes.Length >= 6 => ForItem(BitConverter.ToUInt32(bytes, 1)),
            RankingSubDataKind.OneCount when bytes.Length >= 5 => ForOneCount(BitConverter.ToInt32(bytes, 1)),
            RankingSubDataKind.TwoCounts when bytes.Length >= 9 => ForTwoCounts(
                BitConverter.ToInt32(bytes, 1), BitConverter.ToInt32(bytes, 5)),
            RankingSubDataKind.WinRecord when bytes.Length >= 13 => ForWinRecord(
                BitConverter.ToInt32(bytes, 1), BitConverter.ToInt32(bytes, 5), BitConverter.ToInt32(bytes, 9)),
            RankingSubDataKind.BattleRecord when bytes.Length >= 21 => ForBattleRecord(
                BitConverter.ToInt32(bytes, 1), BitConverter.ToInt32(bytes, 5), BitConverter.ToInt32(bytes, 9),
                BitConverter.ToInt32(bytes, 13), BitConverter.ToInt32(bytes, 17)),
            _ => null
        };
    }
}
