using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class BagExpand
{
    public long? Id { get; set; }

    public byte[] IsBank { get; set; }

    public long? Step { get; set; }

    public long? Price { get; set; }

    public long? ItemId { get; set; }

    public long? ItemCount { get; set; }

    public long? CurrencyId { get; set; }
}
