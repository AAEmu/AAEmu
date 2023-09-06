using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

public partial class CofferItem
{
    public int DecorId { get; set; }

    public long ItemId { get; set; }

    public int Slot { get; set; }
}
