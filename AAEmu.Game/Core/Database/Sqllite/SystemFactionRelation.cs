using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SystemFactionRelation
{
    public long? Id { get; set; }

    public long? Faction1Id { get; set; }

    public long? Faction2Id { get; set; }

    public long? StateId { get; set; }
}
