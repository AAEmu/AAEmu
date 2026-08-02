using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZFactionRelationList (0x014) — [u32 total][u8 count≤200][entry×count].
/// Sets ZoneClient bring-online bit when size==total. Only ids ≥ 100 are stored by Zone.
/// </summary>
public class WZFactionRelationListPacket : ZonePacket
{
    private readonly IReadOnlyList<FactionRelation> _relations;

    public WZFactionRelationListPacket() : this(null) { }

    public WZFactionRelationListPacket(IReadOnlyList<FactionRelation> relations) : base(WzOpcodes.FactionRelationList)
    {
        _relations = relations ?? Array.Empty<FactionRelation>();
    }

    public static WZFactionRelationListPacket FromGame()
    {
        try
        {
            var all = FactionManager.Instance.GetZoneRelations();
            if (all == null || all.Count == 0)
                return new WZFactionRelationListPacket();
            return new WZFactionRelationListPacket(all.Take(200).ToList());
        }
        catch
        {
            return new WZFactionRelationListPacket();
        }
    }

    protected override void WriteBody(PacketStream stream)
    {
        var count = (byte)Math.Min(200, _relations.Count);
        stream.Write((uint)count);
        stream.Write(count);
        for (var i = 0; i < count; i++)
        {
            var r = _relations[i];
            var id = (uint)r.Id;
            var id2 = (uint)r.Id2;
            if (id > id2)
                (id, id2) = (id2, id);
            stream.Write(id);
            stream.Write(id2);
            stream.Write((byte)r.State);
            stream.Write((byte)r.State); // nState
            stream.Write(0ul); // updateTime
            stream.Write(0ul); // changeTime
            stream.Write(0L); // updaterId
            stream.Write(""); // updaterName
            stream.Write(0L); // confirmerId
            stream.Write(""); // confirmerName
        }
    }
}
