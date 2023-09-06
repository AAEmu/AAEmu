using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class PlotCondition
{
    public long? Id { get; set; }

    public byte[] NotCondition { get; set; }

    public long? KindId { get; set; }

    public long? Param1 { get; set; }

    public long? Param2 { get; set; }

    public long? Param3 { get; set; }
}
