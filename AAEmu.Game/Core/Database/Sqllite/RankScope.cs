using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class RankScope
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public string Comment { get; set; }

    public long? ScopeFrom { get; set; }

    public long? ScopeTo { get; set; }
}
