using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class WorldSpecConfig
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? WorldId { get; set; }

    public long? SpecialtyMod { get; set; }

    public long? SpecialtyAdjustRatio { get; set; }
}
