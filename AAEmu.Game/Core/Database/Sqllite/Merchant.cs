using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Merchant
{
    public long? Id { get; set; }

    public long? NpcId { get; set; }

    public long? MerchantPackId { get; set; }
}
