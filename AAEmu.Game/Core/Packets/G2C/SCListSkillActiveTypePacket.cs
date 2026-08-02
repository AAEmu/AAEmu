using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Sends the character's complete skill-active-type mapping list.</summary>
/// <remarks>
/// i32 skillType, u8 activeType.
/// </remarks>
public class SCListSkillActiveTypePacket : GamePacket
{
    public const int MaxEntries = 200;

    private readonly SkillActiveTypeEntry[] _entries;

    public SCListSkillActiveTypePacket(IEnumerable<SkillActiveTypeEntry> entries)
        : base(SCOffsets.SCListSkillActiveTypePacket, 1)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _entries = entries.ToArray();
        if (_entries.Length > MaxEntries)
        {
            throw new ArgumentOutOfRangeException(
                nameof(entries),
                _entries.Length,
                $"The native client accepts at most {MaxEntries} skill-active-type entries per packet.");
        }
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((uint)_entries.Length);
        foreach (var entry in _entries)
        {
            stream.Write(entry.HeirSkillType);
            stream.Write(entry.SkillType);
            stream.Write(entry.ActiveType);
        }

        return stream;
    }
}
