using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.World.Core.Network;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZFactionRelationList (0x014) — [u32 total][u8 count≤200][entry×count].
/// The receiver accumulates chunks until its stored count reaches total.
/// </summary>
public class WZFactionRelationListPacket : ZonePacket
{
    public const int MaxEntriesPerPacket = 200;

    private readonly IReadOnlyList<FactionRelation> _relations;
    private readonly uint _total;

    public WZFactionRelationListPacket() : this(0, [])
    {
    }

    private WZFactionRelationListPacket(uint total, IReadOnlyList<FactionRelation> relations)
        : base(WzOpcodes.FactionRelationList)
    {
        _total = total;
        _relations = relations ?? [];
        if (_relations.Count > MaxEntriesPerPacket)
            throw new ArgumentOutOfRangeException(nameof(relations), "Use SendAllFromGame to chunk.");
    }

    public static void SendAllFromGame(ZoneConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        IReadOnlyList<FactionRelation> all;
        try
        {
            all = FactionManager.Instance.GetZoneRelations()
                .OrderBy(relation => Math.Min((uint)relation.Id, (uint)relation.Id2))
                .ThenBy(relation => Math.Max((uint)relation.Id, (uint)relation.Id2))
                .ToList();
        }
        catch
        {
            all = [];
        }

        if (all.Count == 0)
        {
            connection.SendPacket(new WZFactionRelationListPacket());
            return;
        }

        var total = checked((uint)all.Count);
        for (var offset = 0; offset < all.Count; offset += MaxEntriesPerPacket)
        {
            var take = Math.Min(MaxEntriesPerPacket, all.Count - offset);
            connection.SendPacket(new WZFactionRelationListPacket(total, all.Skip(offset).Take(take).ToList()));
        }
    }

    protected override void WriteBody(PacketStream stream)
    {
        var count = checked((byte)_relations.Count);
        stream.Write(_total);
        stream.Write(count);
        for (var i = 0; i < count; i++)
        {
            var relation = _relations[i];
            var id = (uint)relation.Id;
            var id2 = (uint)relation.Id2;
            if (id > id2)
                (id, id2) = (id2, id);
            stream.Write(id);
            stream.Write(id2);
            stream.Write((byte)relation.State);
            stream.Write((byte)relation.State); // nState
            stream.Write(0ul); // updateTime
            stream.Write(0ul); // changeTime
            stream.Write(0L); // updaterId
            stream.Write(""); // updaterName
            stream.Write(0L); // confirmerId
            stream.Write(""); // confirmerName
        }
    }
}
