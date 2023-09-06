using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class PlotAoeCondition
{
    public long? Id { get; set; }

    public long? EventId { get; set; }

    public long? ConditionId { get; set; }

    public long? Position { get; set; }
}
