using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SpecialEffect
{
    public long? Id { get; set; }

    public long? SpecialEffectTypeId { get; set; }

    public long? Value1 { get; set; }

    public long? Value2 { get; set; }

    public long? Value3 { get; set; }

    public long? Value4 { get; set; }
}
