using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Level
{
    public long? Id { get; set; }

    public long? TotalExp { get; set; }

    public long? TotalMateExp { get; set; }

    public long? SkillPoints { get; set; }
}
