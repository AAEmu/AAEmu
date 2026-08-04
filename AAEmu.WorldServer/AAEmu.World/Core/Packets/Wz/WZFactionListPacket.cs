using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.World.Core.Network;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZFactionList (0x013) — [u32 total][u8 count≤20][FactionDesc×count].
/// The receiver accumulates chunks until its stored count reaches total.
/// </summary>
public class WZFactionListPacket : ZonePacket
{
    public const int MaxEntriesPerPacket = 20;

    private readonly IReadOnlyList<SystemFaction> _factions;
    private readonly uint _total;

    public WZFactionListPacket() : this(0, [])
    {
    }

    private WZFactionListPacket(uint total, IReadOnlyList<SystemFaction> factions) : base(WzOpcodes.FactionList)
    {
        _total = total;
        _factions = factions ?? [];
        if (_factions.Count > MaxEntriesPerPacket)
            throw new ArgumentOutOfRangeException(nameof(factions), "Use SendAllFromGame to chunk.");
    }

    public static void SendAllFromGame(ZoneConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        IReadOnlyList<SystemFaction> all;
        try
        {
            all = FactionManager.Instance.GetSystemFactions()
                .OrderBy(faction => (uint)faction.Id)
                .ToList();
        }
        catch
        {
            all = [];
        }

        if (all.Count == 0)
        {
            connection.SendPacket(new WZFactionListPacket());
            return;
        }

        var total = checked((uint)all.Count);
        for (var offset = 0; offset < all.Count; offset += MaxEntriesPerPacket)
        {
            var take = Math.Min(MaxEntriesPerPacket, all.Count - offset);
            connection.SendPacket(new WZFactionListPacket(total, all.Skip(offset).Take(take).ToList()));
        }
    }

    protected override void WriteBody(PacketStream stream)
    {
        var count = checked((byte)_factions.Count);
        stream.Write(_total);
        stream.Write(count);
        for (var i = 0; i < count; i++)
            stream.Write(_factions[i]);
    }
}
