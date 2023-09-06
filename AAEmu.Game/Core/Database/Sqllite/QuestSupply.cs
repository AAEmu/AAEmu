using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestSupply
{
    public long? Id { get; set; }

    public long? Level { get; set; }

    public long? Exp { get; set; }

    public long? Copper { get; set; }
}
