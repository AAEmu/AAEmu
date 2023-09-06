using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DemoChar
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? RaceId { get; set; }

    public long? GenderId { get; set; }

    public long? DemoId { get; set; }

    public long? DemoEquipId { get; set; }

    public long? DemoBagId { get; set; }

    public long? DemoStartLocId { get; set; }

    public long? FactionId { get; set; }
}
