using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class RaceTrackShape
{
    public long? Id { get; set; }

    public long? RaceTrackId { get; set; }

    public long? ShapeOrder { get; set; }

    public long? V1 { get; set; }
}
