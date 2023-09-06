using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncBuff
{
    public long? Id { get; set; }

    public long? BuffId { get; set; }

    public double? Radius { get; set; }

    public long? Count { get; set; }

    public long? PermId { get; set; }

    public long? RelationshipId { get; set; }
}
