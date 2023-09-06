using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncCraftGradeRatio
{
    public long? Id { get; set; }

    public long? GradeId { get; set; }

    public long? Ratio { get; set; }
}
