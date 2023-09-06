using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class WearableFormula
{
    public long? Id { get; set; }

    public long? KindId { get; set; }

    public string Formula { get; set; }
}
