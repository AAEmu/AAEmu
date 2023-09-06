using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncRatioChange
{
    public long? Id { get; set; }

    public long? Ratio { get; set; }

    public long? NextPhase { get; set; }
}
