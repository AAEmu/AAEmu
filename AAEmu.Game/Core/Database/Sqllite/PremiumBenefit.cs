using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class PremiumBenefit
{
    public long? Id { get; set; }

    public long? OnlineLabor { get; set; }

    public long? OfflineLabor { get; set; }

    public long? MaxLabor { get; set; }

    public long? IconId { get; set; }

    public long? GradeId { get; set; }
}
