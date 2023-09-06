using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ZoneGroupBannedTag
{
    public long? Id { get; set; }

    public long? ZoneGroupId { get; set; }

    public long? TagId { get; set; }

    public long? BannedPeriodsId { get; set; }

    public string Usage { get; set; }
}
