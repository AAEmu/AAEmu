using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class FarmGroup
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? Count { get; set; }

    public string Description { get; set; }
}
