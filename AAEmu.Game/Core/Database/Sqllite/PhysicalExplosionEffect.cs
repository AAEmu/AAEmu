using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class PhysicalExplosionEffect
{
    public long? Id { get; set; }

    public double? Radius { get; set; }

    public double? HoleSize { get; set; }

    public double? Pressure { get; set; }
}
