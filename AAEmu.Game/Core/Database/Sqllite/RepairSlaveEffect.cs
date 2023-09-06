using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class RepairSlaveEffect
{
    public long? Id { get; set; }

    public long? Health { get; set; }

    public long? Mana { get; set; }
}
