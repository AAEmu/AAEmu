using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ScopedFEffect
{
    public long? Id { get; set; }

    public long? Range { get; set; }

    public string Key { get; set; }

    public long? DoodadId { get; set; }
}
