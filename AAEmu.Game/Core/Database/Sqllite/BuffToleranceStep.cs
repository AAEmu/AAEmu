using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class BuffToleranceStep
{
    public long? Id { get; set; }

    public long? BuffToleranceId { get; set; }

    public long? HitChance { get; set; }

    public long? TimeReduction { get; set; }
}
