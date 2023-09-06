using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SiegePlan
{
    public long? Id { get; set; }

    public long? ZoneGroupId { get; set; }

    public byte[] WeekStart { get; set; }
}
