using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class MerchantPriceRatio
{
    public long? Id { get; set; }

    public long? NpcId { get; set; }

    public long? Ratio { get; set; }
}
