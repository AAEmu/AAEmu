using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class AggroEffect
{
    public long? Id { get; set; }

    public byte[] UseFixedAggro { get; set; }

    public long? FixedMin { get; set; }

    public long? FixedMax { get; set; }

    public byte[] UseLevelAggro { get; set; }

    public double? LevelMd { get; set; }

    public long? LevelVaStart { get; set; }

    public long? LevelVaEnd { get; set; }

    public byte[] UseChargedBuff { get; set; }

    public long? ChargedBuffId { get; set; }

    public double? ChargedMul { get; set; }
}
