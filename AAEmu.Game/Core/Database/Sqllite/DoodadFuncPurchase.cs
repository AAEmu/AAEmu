using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncPurchase
{
    public long? Id { get; set; }

    public long? ItemId { get; set; }

    public long? Count { get; set; }

    public long? CoinItemId { get; set; }

    public long? CoinCount { get; set; }

    public long? CurrencyId { get; set; }
}
