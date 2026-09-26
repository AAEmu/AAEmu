using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFactionRelationListPacket : GamePacket
{
    /// <summary>The client reads at most 200 entries per packet.</summary>
    public const int MaxEntriesPerPacket = 200;

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
        stream.Write((byte)_relations.Length);
        foreach (var relation in _relations)
        {
            FactionRelationWire.Write(stream,
                (uint)relation.Id, (uint)relation.Id2, relation.State,
                // A plain content row keeps the nState 0 this packet always sent; an agreement carries what it reverts to.
                relation.HasDiplomacy ? relation.NextState : 0,
                relation.UpdateTime, relation.ChangeTime,
                relation.UpdaterId, relation.UpdaterName, relation.ConfirmerId, relation.ConfirmerName);
        }

        return stream;
    }
}

/// <summary>
/// One relation entry as the client reads it , shared by
/// SCFactionRelationList, SCFactionRelationHistory and WZFactionRelationList):
/// type(i32) | type(i32) | state(i8) | nState(i8) | updateTime(i64) | changeTime(i64) |
/// type(u64) updaterId | updaterName(str) | type(u64) confirmerId | confirmerName(str).
/// The client sorts the two ids ascending after reading them.
/// </summary>
public static class FactionRelationWire
{
    public static void Write(PacketStream stream, uint id, uint id2, RelationState state, RelationState nextState,
        DateTime updateTime, DateTime changeTime, ulong updaterId, string updaterName, ulong confirmerId, string confirmerName)
    {
        stream.Write(id);
        stream.Write(id2);
        stream.Write((byte)state);
        stream.Write((byte)nextState);
        stream.Write(updateTime);
        stream.Write(changeTime);
        stream.Write(updaterId);
        stream.Write(updaterName ?? string.Empty);
        stream.Write(confirmerId);
        stream.Write(confirmerName ?? string.Empty);
    }

    public static void Write(PacketStream stream, FactionDiplomacyAgreement entry) =>
        Write(stream, entry.Faction1, entry.Faction2, entry.State, entry.NextState, entry.UpdateTime, entry.ChangeTime,
            entry.UpdaterId, entry.UpdaterName, entry.ConfirmerId, entry.ConfirmerName);
}
