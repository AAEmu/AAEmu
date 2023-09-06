using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncWaterVolume
{
    public long? Id { get; set; }

    public double? LevelChange { get; set; }

    public double? Duration { get; set; }
}
