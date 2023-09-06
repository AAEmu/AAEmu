using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Loot
{
    public long? Id { get; set; }

    public long? Group { get; set; }

    public long? ItemId { get; set; }

    public long? DropRate { get; set; }

    public long? MinAmount { get; set; }

    public long? MaxAmount { get; set; }

    public long? LootPackId { get; set; }

    public long? GradeId { get; set; }

    public string AlwaysDrop { get; set; }
}
