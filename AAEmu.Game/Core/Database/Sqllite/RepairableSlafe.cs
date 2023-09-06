using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class RepairableSlafe
{
    public long? Id { get; set; }

    public long? RepairSlaveEffectId { get; set; }

    public long? SlaveId { get; set; }
}
