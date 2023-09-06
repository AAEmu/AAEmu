using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SiegeSetting
{
    public long? TotalCastles { get; set; }

    public long? NumDefenders { get; set; }

    public long? NumReinforcements { get; set; }
}
