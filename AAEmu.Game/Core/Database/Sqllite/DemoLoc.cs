using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DemoLoc
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? ZoneId { get; set; }

    public long? X { get; set; }

    public long? Y { get; set; }

    public long? Z { get; set; }
}
