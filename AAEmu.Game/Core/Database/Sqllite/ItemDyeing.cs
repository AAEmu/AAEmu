using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemDyeing
{
    public long? Id { get; set; }

    public long? ItemId { get; set; }

    public long? DyeingColorId { get; set; }
}
