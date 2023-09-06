using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DyeableItem
{
    public long? Id { get; set; }

    public long? ItemId { get; set; }

    public long? DefaultDyeingItemId { get; set; }
}
