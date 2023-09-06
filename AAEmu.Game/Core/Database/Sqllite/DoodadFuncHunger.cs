using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncHunger
{
    public long? Id { get; set; }

    public long? HungryTerm { get; set; }

    public long? FullStep { get; set; }

    public long? PhaseChangeLimit { get; set; }

    public long? NextPhase { get; set; }
}
