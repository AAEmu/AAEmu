using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ResurrectionWaitingTime
{
    public long? Id { get; set; }

    public long? PenaltyDuration { get; set; }

    public long? WaitingTime { get; set; }

    public long? SiegeWaitingTime { get; set; }
}
