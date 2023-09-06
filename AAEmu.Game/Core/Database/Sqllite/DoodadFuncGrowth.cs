using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncGrowth
{
    public long? Id { get; set; }

    public long? Delay { get; set; }

    public long? StartScale { get; set; }

    public long? EndScale { get; set; }

    public long? NextPhase { get; set; }
}
