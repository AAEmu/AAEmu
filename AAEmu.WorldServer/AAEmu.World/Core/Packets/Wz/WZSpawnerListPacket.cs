using AAEmu.Commons.Network;
using AAEmu.World.Core.Network;
using AAEmu.World.Core.Relay;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZSpawnerList (0x005) — saved indun spawner state.
/// Wire: [u8 last][u8 count≤100][{u32 id, u32 type, u8 state}×count] (9 B/entry).
/// Layout and the 100-entry clamp are verified against the packet serializer
/// field names are "last", "count", "id", "type", "state".
/// Empty last=1 chunk opens the join gate and is the correct payload for seamless worlds —
/// see <see cref="ZoneNpcSpawnerCatalog"/> for the acceptance rules dedicate applies.
/// </summary>
public class WZSpawnerListPacket : ZonePacket
{
    public const int MaxEntriesPerPacket = 100;

    private readonly IReadOnlyList<ZoneNpcSpawnerCatalog.PersistentSpawnerEntry> _entries;
    private readonly bool _last;

    public WZSpawnerListPacket()
        : this([], last: true)
    {
    }

    public WZSpawnerListPacket(
        IReadOnlyList<ZoneNpcSpawnerCatalog.PersistentSpawnerEntry> entries,
        bool last)
        : base(WzOpcodes.SpawnerList)
    {
        _entries = entries ?? [];
        _last = last;
        if (_entries.Count > MaxEntriesPerPacket)
            throw new ArgumentOutOfRangeException(nameof(entries), "Use SendAll to chunk.");
    }

    /// <summary>Send one or more SpawnerList packets; final chunk always has last=1.</summary>
    public static void SendAll(
        ZoneConnection connection,
        IReadOnlyList<ZoneNpcSpawnerCatalog.PersistentSpawnerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(connection);
        entries ??= [];
        if (entries.Count == 0)
        {
            connection.SendPacket(new WZSpawnerListPacket([], last: true));
            return;
        }

        for (var offset = 0; offset < entries.Count; offset += MaxEntriesPerPacket)
        {
            var take = Math.Min(MaxEntriesPerPacket, entries.Count - offset);
            var chunk = new ZoneNpcSpawnerCatalog.PersistentSpawnerEntry[take];
            for (var i = 0; i < take; i++)
                chunk[i] = entries[offset + i];
            var last = offset + take >= entries.Count;
            connection.SendPacket(new WZSpawnerListPacket(chunk, last));
        }
    }

    protected override void WriteBody(PacketStream stream)
    {
        stream.Write((byte)(_last ? 1 : 0));
        stream.Write((byte)_entries.Count);
        foreach (var entry in _entries)
        {
            stream.Write(entry.Id);
            stream.Write(entry.Type);
            stream.Write(entry.State);
        }
    }
}
