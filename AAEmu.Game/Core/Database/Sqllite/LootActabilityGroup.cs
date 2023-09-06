using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class LootActabilityGroup
{
    public long? Id { get; set; }

    public long? LootPackId { get; set; }

    public long? LootGroupId { get; set; }

    public long? MaxDice { get; set; }

    public long? MinDice { get; set; }
}
