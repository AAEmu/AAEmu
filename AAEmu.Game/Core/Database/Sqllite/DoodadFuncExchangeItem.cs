using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncExchangeItem
{
    public long? Id { get; set; }

    public long? DoodadFuncExchangeId { get; set; }

    public long? ItemId { get; set; }

    public long? LootPackId { get; set; }
}
