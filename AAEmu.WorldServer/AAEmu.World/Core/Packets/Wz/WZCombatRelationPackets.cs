using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// Shared WZ combat-relation packet body: one count byte followed by four uint32 fields per record.
/// </summary>
public abstract class WZCombatRelationPacket : ZonePacket
{
    public const int EntrySize = 16;
    public const int MaxEntriesPerPacket = byte.MaxValue;

    private readonly CombatRelationEntry[] _entries;

    protected WZCombatRelationPacket(ushort opcode, IReadOnlyList<CombatRelationEntry> entries)
        : base(opcode)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count > MaxEntriesPerPacket)
            throw new ArgumentOutOfRangeException(nameof(entries), $"A relation packet carries at most {MaxEntriesPerPacket} records.");

        _entries = entries.ToArray();
    }

    /// <summary>Decodes and validates a complete WZ combat-relation body.</summary>
    public static IReadOnlyList<CombatRelationEntry> Decode(PacketStream body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.Count < sizeof(byte))
            throw new InvalidDataException("Combat-relation packet is missing its record count.");

        var count = body.ReadByte();
        var expected = checked((int)count * EntrySize);
        var remaining = body.Count - body.Pos;
        if (remaining != expected)
            throw new InvalidDataException($"Combat-relation packet declares {count} records but contains {remaining} payload bytes.");

        var entries = new CombatRelationEntry[count];
        for (var i = 0; i < count; i++)
        {
            entries[i] = new CombatRelationEntry(
                body.ReadUInt32(),
                body.ReadUInt32(),
                body.ReadUInt32(),
                body.ReadUInt32());
        }

        return entries;
    }

    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(checked((byte)_entries.Length));
        foreach (var entry in _entries)
        {
            stream.Write(entry.Faction1);
            stream.Write(entry.Faction2);
            stream.Write(entry.RelationType);
            stream.Write(entry.Flags);
        }
    }
}

/// <summary>WZ CvF combat relationships (opcode 0x027).</summary>
public sealed class WZCvFCombatRelationshipPacket : WZCombatRelationPacket
{
    public WZCvFCombatRelationshipPacket()
        : this([])
    {
    }

    public WZCvFCombatRelationshipPacket(IReadOnlyList<CombatRelationEntry> entries)
        : base(WzOpcodes.CvFCombatRelationship, entries)
    {
    }
}

/// <summary>WZ FvF combat relationships (opcode 0x028).</summary>
public sealed class WZFvFCombatRelationshipPacket : WZCombatRelationPacket
{
    public WZFvFCombatRelationshipPacket()
        : this([])
    {
    }

    public WZFvFCombatRelationshipPacket(IReadOnlyList<CombatRelationEntry> entries)
        : base(WzOpcodes.FvFCombatRelationship, entries)
    {
    }
}
