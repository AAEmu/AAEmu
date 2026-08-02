using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>The native aggro value associated with one hostile unit.</summary>
public readonly record struct AiAggroEntry(
    uint HostileUnitId,
    int Value1,
    int Value2,
    int Value3,
    byte TopFlags)
{
    /// <summary>
    /// </summary>
    public static AiAggroEntry FromDamageValue(uint hostileUnitId, int damage)
        => new(hostileUnitId, damage, default, default, default);

    /// <summary>
    /// </summary>
    public static AiAggroEntry FromDirectValue(uint hostileUnitId, int direct)
        => new(hostileUnitId, default, default, direct, default);
}

/// <summary>
/// Native <c>unitAiAggro</c>: <c>bc npcId, u32 count</c>, followed by <c>count</c>
/// entries of <c>bc hostileUnitId, i32 value[3], i8 topFlags</c>.
/// </summary>
/// <remarks>
/// clamps the receive count to the 100 entries allocated by the native packet object.
/// </remarks>
public class SCAiAggroPacket : GamePacket
{
    public const int MaxEntries = 100;

    private readonly uint _npcId;
    private readonly AiAggroEntry[] _entries;

    public SCAiAggroPacket(uint npcId)
        : this(npcId, Array.Empty<AiAggroEntry>())
    {
    }

    public SCAiAggroPacket(uint npcId, AiAggroEntry entry)
        : this(npcId, [entry])
    {
    }

    public SCAiAggroPacket(uint npcId, IEnumerable<AiAggroEntry> entries)
        : base(SCOffsets.SCAiAggroPacket, 1)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _npcId = npcId;
        _entries = entries.ToArray();
        if (_entries.Length > MaxEntries)
        {
            throw new ArgumentOutOfRangeException(
                nameof(entries),
                _entries.Length,
                $"The native client accepts at most {MaxEntries} aggro entries per packet.");
        }
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_npcId);
        stream.Write((uint)_entries.Length);

        foreach (var entry in _entries)
        {
            stream.WriteBc(entry.HostileUnitId);
            stream.Write(entry.Value1);
            stream.Write(entry.Value2);
            stream.Write(entry.Value3);
            stream.Write(entry.TopFlags);
        }

        return stream;
    }
}
