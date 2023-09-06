using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ManaBurnEffect
{
    public long? Id { get; set; }

    public long? BaseMin { get; set; }

    public long? BaseMax { get; set; }

    public long? DamageRatio { get; set; }

    public double? LevelMd { get; set; }

    public long? LevelVaStart { get; set; }

    public long? LevelVaEnd { get; set; }
}
