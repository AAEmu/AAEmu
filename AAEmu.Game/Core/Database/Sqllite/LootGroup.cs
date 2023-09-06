using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class LootGroup
{
    public long? Id { get; set; }

    public long? PackId { get; set; }

    public long? GroupNo { get; set; }

    public long? DropRate { get; set; }

    public long? ItemGradeDistributionId { get; set; }
}
