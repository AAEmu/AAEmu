using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class UnitAttributeLimit
{
    public long? Id { get; set; }

    public long? UnitAttributeId { get; set; }

    public long? Minimum { get; set; }

    public long? Maximum { get; set; }
}
