using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class FxMaterial
{
    public long? Id { get; set; }

    public long? CustomDualMaterialId { get; set; }

    public double? CustomDualMaterialFadeTime { get; set; }
}
