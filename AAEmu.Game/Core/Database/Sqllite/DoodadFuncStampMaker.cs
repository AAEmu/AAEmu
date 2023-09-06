using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncStampMaker
{
    public long? Id { get; set; }

    public long? ConsumeMoney { get; set; }

    public long? ItemId { get; set; }

    public long? ConsumeItemId { get; set; }

    public long? ConsumeCount { get; set; }
}
