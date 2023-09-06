using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class PremiumGrade
{
    public long? Id { get; set; }

    public long? GradeId { get; set; }

    public long? Point { get; set; }
}
