using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadPhaseFunc
{
    public long? Id { get; set; }

    public long? DoodadFuncGroupId { get; set; }

    public long? ActualFuncId { get; set; }

    public string ActualFuncType { get; set; }
}
