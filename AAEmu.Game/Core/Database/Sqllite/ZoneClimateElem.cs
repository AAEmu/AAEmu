using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ZoneClimateElem
{
    public long? Id { get; set; }

    public long? ZoneClimateId { get; set; }

    public long? ClimateId { get; set; }
}
