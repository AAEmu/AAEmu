using System;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Faction;

public class FactionRelation
{
    public FactionsEnum Id { get; set; }
    public FactionsEnum Id2 { get; set; }
    public RelationState State { get; set; }
    public DateTime ExpTime { get; set; }
}
