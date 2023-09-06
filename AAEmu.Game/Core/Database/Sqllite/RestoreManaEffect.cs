using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class RestoreManaEffect
{
    public long? Id { get; set; }

    public byte[] UseFixedValue { get; set; }

    public long? FixedMin { get; set; }

    public long? FixedMax { get; set; }

    public byte[] UseLevelValue { get; set; }

    public double? LevelMd { get; set; }

    public long? LevelVaStart { get; set; }

    public long? LevelVaEnd { get; set; }

    public byte[] Percent { get; set; }
}
