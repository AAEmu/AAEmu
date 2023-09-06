using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class WiDetail
{
    public long? Id { get; set; }

    public long? WiId { get; set; }

    public long? Lp { get; set; }

    public byte[] ApplyExpert { get; set; }

    public long? DistanceSqrt { get; set; }
}
