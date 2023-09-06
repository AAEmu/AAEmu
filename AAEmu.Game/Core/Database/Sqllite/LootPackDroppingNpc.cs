using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class LootPackDroppingNpc
{
    public long? Id { get; set; }

    public long? NpcId { get; set; }

    public long? LootPackId { get; set; }

    public string DefaultPack { get; set; }
}
