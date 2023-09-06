using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class UnitFormula
{
    public long? Id { get; set; }

    public string Formula { get; set; }

    public long? KindId { get; set; }

    public long? OwnerTypeId { get; set; }
}
