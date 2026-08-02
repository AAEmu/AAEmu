using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>One fixed-width entry in a character's Heir-skill list.</summary>
public readonly record struct HeirSkillListEntry(
    int HeirSkillId,
    int BaseSkillId,
    int SuccessorSkillId,
    uint SkillLevel,
    sbyte Ability,
    sbyte ActiveType);

/// <summary>Sends the character's authoritative Heir successor selections.</summary>
/// <remarks>
/// i32 baseSkillId, i32 successorSkillId, u32 skillLevel, i8 ability, i8 activeType.
/// </remarks>
public class SCHeirSkillListPacket : GamePacket
{
    public const int MaxEntries = 128;

    private readonly HeirSkillListEntry[] _entries;

    public SCHeirSkillListPacket(IEnumerable<HeirSkillListEntry> entries)
        : base(SCOffsets.SCHeirSkillListPacket, 1)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _entries = entries.ToArray();
        if (_entries.Length > MaxEntries)
        {
            throw new ArgumentOutOfRangeException(
                nameof(entries),
                _entries.Length,
                $"The native client accepts at most {MaxEntries} Heir-skill entries per packet.");
        }
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((uint)_entries.Length);
        foreach (var entry in _entries)
        {
            stream.Write(entry.HeirSkillId);
            stream.Write(entry.BaseSkillId);
            stream.Write(entry.SuccessorSkillId);
            stream.Write(entry.SkillLevel);
            stream.Write(entry.Ability);
            stream.Write(entry.ActiveType);
        }

        return stream;
    }
}
