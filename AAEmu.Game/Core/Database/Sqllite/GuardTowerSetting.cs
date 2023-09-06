using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class GuardTowerSetting
{
    public long? Id { get; set; }

    public long? RadiusDominion { get; set; }

    public long? RadiusSiege { get; set; }

    public long? RadiusDeclare { get; set; }

    public long? RadiusOffenseHq { get; set; }

    public string Comments { get; set; }

    public long? InitialBuffId { get; set; }

    public long? MaxGates { get; set; }

    public long? MaxWalls { get; set; }
}
