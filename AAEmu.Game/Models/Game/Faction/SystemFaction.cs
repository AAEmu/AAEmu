using System;
using System.Collections.Generic;
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

    public Dictionary<FactionsEnum, FactionRelation> Relations { get; set; } = new();

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

        // Not sure if we should prioritize mother faction here?
        if (MotherId != 0)
        {
            var motherFaction = FactionManager.Instance.GetFaction(MotherId);
            if (motherFaction != null)
            {
                if (motherFaction.Relations.TryGetValue(otherFactionId, out var motherRelation))
                    return motherRelation.State;

                // TODO not found, so enemy (id = [1, 2, 3])?
                return RelationState.Friendly;
            }
        }

        return Relations.TryGetValue(otherFactionId, out var relation) ? relation.State : RelationState.Neutral;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((uint)Id);                // type
        stream.Write((uint)MotherId);          // type
        stream.Write(Name);              // name
        stream.Write(OwnerId);           // ownerId Int32
        stream.Write(OwnerName);         // ownerName
        stream.Write(UnitOwnerType);     // UnitOwnerType Byte
        stream.Write(PoliticalSystem);   // PoliticalSystem Byte
        stream.Write(Created);           // createdTime
        stream.Write(AggroLink);         // aggroLink
        stream.Write(DiplomacyTarget);   // dTarget
        stream.Write(AllowChangeName);   // allowChangeName
        stream.Write(0L);                // renameTime
        return stream;
    }
}
