using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Faction;

public class SystemFaction : PacketMarshaler
{
    public FactionsEnum Id { get; set; }
    public string Name { get; set; }
    public uint OwnerId { get; set; }
    public string OwnerName { get; set; }
    public FactionsEnum MotherId { get; set; }
    public sbyte UnitOwnerType { get; set; }
    public byte PoliticalSystem { get; set; }
    public bool DiplomacyTarget { get; set; }
    public bool AggroLink { get; set; }
    public bool GuardHelp { get; set; }
    public byte AllowChangeName { get; set; }
    public DateTime Created { get; set; }
    public DateTime RenameTime { get; set; }
    public bool IntegrationFaction { get; set; }

    public Dictionary<FactionsEnum, FactionRelation> Relations { get; set; } = [];

    public RelationState GetRelationState(SystemFaction otherFaction)
    {
        if (otherFaction == null) return RelationState.Neutral;

        var factionId = MotherId != FactionsEnum.Invalid ? MotherId : Id;
        var otherFactionId = otherFaction.MotherId != 0 ? otherFaction.MotherId : otherFaction.Id;

        // Handle Root Factions
        switch (factionId)
        {
            case FactionsEnum.Neutral:
                return RelationState.Neutral;
            case FactionsEnum.Friendly:
                return RelationState.Friendly;
            case FactionsEnum.Hostile:
                return RelationState.Hostile;
        }

        // Handle Target Root Factions
        switch (otherFactionId)
        {
            case FactionsEnum.Neutral:
                return RelationState.Neutral;
            case FactionsEnum.Friendly:
                return RelationState.Friendly;
            case FactionsEnum.Hostile:
                return RelationState.Hostile;
        }

        if (factionId == otherFactionId)
            return RelationState.Friendly;

        // Prefer mother-faction diplomacy when set (e.g. Crescent Throne 101 → Nuia Alliance 148).
        if (MotherId != 0)
        {
            var motherFaction = FactionManager.Instance.GetFaction(MotherId);
            if (motherFaction != null)
            {
                if (motherFaction.Relations.TryGetValue(otherFactionId, out var motherRelation))
                    return motherRelation.State;

                // system_faction_relations is loaded into faction1.Relations[faction2] only.
                // Content often stores a single directed row — e.g. monster 115 → Nuia 148
                // Hostile (state_id=1) with no 148 → 115 twin. Without the reverse lookup,
                // plot Area Hostile filters (shotgun 5796 / continuous fire 5604) see every
                // mob as Friendly and the SetVariable hit-count gate reports no target.
                if (TryGetStoredRelation(otherFaction, MotherId, out var reverseMother))
                    return reverseMother;
                if (TryGetStoredRelation(otherFaction, Id, out var reverseSelf))
                    return reverseSelf;

                // TODO(v10): default when neither direction exists — comment claims enemy, code returns Friendly.
                return RelationState.Friendly;
            }
        }

        if (Relations.TryGetValue(otherFactionId, out var relation))
            return relation.State;
        if (TryGetStoredRelation(otherFaction, factionId, out var reverse))
            return reverse;
        if (Id != factionId && TryGetStoredRelation(otherFaction, Id, out var reverseId))
            return reverseId;
        return RelationState.Neutral;
    }

    /// <summary>
    /// Reads a relation row indexed on <paramref name="from"/> as faction1 toward <paramref name="towardId"/>.
    /// </summary>
    private static bool TryGetStoredRelation(SystemFaction from, FactionsEnum towardId, out RelationState state)
    {
        if (from.Relations.TryGetValue(towardId, out var relation))
        {
            state = relation.State;
            return true;
        }

        state = default;
        return false;
    }

    public override PacketStream Write(PacketStream stream)
    {
        // SCExpeditionList, SCFactionCreated, and the Zone faction list.
        stream.Write((uint)Id);
        stream.Write((uint)MotherId);
        stream.Write(Name);
        stream.Write((ulong)OwnerId);
        stream.Write(OwnerName);
        stream.Write(UnitOwnerType);
        stream.Write(PoliticalSystem);
        stream.Write(Created);
        stream.Write(AggroLink);
        stream.Write(DiplomacyTarget);
        stream.Write(AllowChangeName);
        stream.Write(RenameTime);
        stream.Write(IntegrationFaction);
        return stream;
    }
}
