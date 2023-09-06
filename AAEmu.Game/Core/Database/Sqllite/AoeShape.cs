using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class AoeShape
{
    public long? Id { get; set; }

    public long? KindId { get; set; }

    public double? Value1 { get; set; }

    public double? Value2 { get; set; }

    public double? Value3 { get; set; }
}
