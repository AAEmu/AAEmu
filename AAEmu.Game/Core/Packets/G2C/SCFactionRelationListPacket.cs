using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFactionRelationListPacket : GamePacket
{
    private readonly FactionRelation[] _relations;

    public SCFactionRelationListPacket() : base(SCOffsets.SCFactionRelationListPacket, 1)
    {
        _relations = [];
    }

    public SCFactionRelationListPacket(FactionRelation[] relations) : base(SCOffsets.SCFactionRelationListPacket, 1)
    {
        _relations = relations;
    }

    public override PacketStream Write(PacketStream stream)
    {
        // 10.0.2.13 body: count(u8, capped at 200) then per relation:
        // id(u32) | id2(u32) | state(u8) | nState(u8) | updateTime(i64) | changeTime(i64) |
        // updaterId(i64) | updaterName(str) | confirmerId(i64) | confirmerName(str).
        stream.Write((byte)_relations.Length);
        foreach (var relation in _relations)
        {
            stream.Write((uint)relation.Id);        // "type" (faction1 id)
            stream.Write((uint)relation.Id2);       // "type" (faction2 id)
            stream.Write((byte)relation.State);     // "state"
            stream.Write((byte)0);                  // "nState" (pending relation state)
            stream.Write(DateTime.MinValue);        // "updateTime"
            stream.Write(DateTime.MinValue);        // "changeTime"
            stream.Write(0L);                       // "type" (updaterId)
            stream.Write("");                       // "updaterName"
            stream.Write(0L);                       // "type" (confirmerId)
            stream.Write("");                       // "confirmerName"
        }

        return stream;
    }
}
