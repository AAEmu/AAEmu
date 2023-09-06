using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DistrictReturnPoint
{
    public long? Id { get; set; }

    public long? DistrictId { get; set; }

    public long? FactionId { get; set; }

    public long? ReturnPointId { get; set; }
}
