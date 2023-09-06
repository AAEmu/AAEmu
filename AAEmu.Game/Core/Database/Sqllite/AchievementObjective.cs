using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class AchievementObjective
{
    public long? Id { get; set; }

    public long? AchievementId { get; set; }

    public byte[] OrUnitReqs { get; set; }

    public long? RecordId { get; set; }
}
