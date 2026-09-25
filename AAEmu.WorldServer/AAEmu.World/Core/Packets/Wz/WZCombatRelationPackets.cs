using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// Shared count prefix. Each record is 10 bytes: a character relation is one u64 key plus two u8
/// fields, and a faction relation is two i32 ids plus the same two u8 fields.
/// </summary>
public abstract class WZCombatRelationPacket : ZonePacket
{
    public const int EntrySize = 10;
    public const int MaxEntriesPerPacket = byte.MaxValue;

    private readonly CombatRelationEntry[] _entries;
    private readonly bool _characterKey;

    protected WZCombatRelationPacket(ushort opcode, IReadOnlyList<CombatRelationEntry> entries, bool characterKey)
        : base(opcode)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count > MaxEntriesPerPacket)
            throw new ArgumentOutOfRangeException(nameof(entries), $"A relation packet carries at most {MaxEntriesPerPacket} records.");

        _entries = entries.ToArray();
        _characterKey = characterKey;
    }

    public static IReadOnlyList<CombatRelationEntry> Decode(PacketStream body, bool characterKey)
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
            entries[i] = characterKey ? ReadCharacter(body) : ReadFaction(body);

        return entries;
    }

    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(checked((byte)_entries.Length));
        foreach (var entry in _entries)
        {
            if (_characterKey)
            {
                stream.Write((ulong)entry.Faction1);
            }
            else
            {
                stream.Write((int)entry.Faction1);
                stream.Write((int)entry.Faction2);
            }

            stream.Write(entry.Code);
            stream.Write(entry.Reason);
        }
    }

    private static CombatRelationEntry ReadCharacter(PacketStream body)
    {
        var key = body.ReadUInt64();
        if (key > uint.MaxValue)
            throw new InvalidDataException("Character relation key does not fit the publication id.");

        return new CombatRelationEntry((uint)key, 0, body.ReadByte(), body.ReadByte());
    }

    private static CombatRelationEntry ReadFaction(PacketStream body) =>
        new((uint)body.ReadInt32(), (uint)body.ReadInt32(), body.ReadByte(), body.ReadByte());
}

/// <summary>WZ character-versus-faction relationships (opcode 0x027).</summary>
public sealed class WZCvFCombatRelationshipPacket : WZCombatRelationPacket
{
    public WZCvFCombatRelationshipPacket()
        : this([])
    {
    }

    public WZCvFCombatRelationshipPacket(IReadOnlyList<CombatRelationEntry> entries)
        : base(WzOpcodes.CvFCombatRelationship, entries, characterKey: true)
    {
    }
}

/// <summary>WZ faction-versus-faction relationships (opcode 0x028).</summary>
public sealed class WZFvFCombatRelationshipPacket : WZCombatRelationPacket
{
    public WZFvFCombatRelationshipPacket()
        : this([])
    {
    }

    public WZFvFCombatRelationshipPacket(IReadOnlyList<CombatRelationEntry> entries)
        : base(WzOpcodes.FvFCombatRelationship, entries, characterKey: false)
    {
    }
}
