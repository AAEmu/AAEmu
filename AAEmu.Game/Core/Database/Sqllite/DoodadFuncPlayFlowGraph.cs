using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncPlayFlowGraph
{
    public long? Id { get; set; }

    public long? EventOnPhaseChangeId { get; set; }

    public long? EventOnVisibleId { get; set; }
}
