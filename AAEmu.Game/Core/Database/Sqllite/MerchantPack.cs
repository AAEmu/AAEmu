using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class MerchantPack
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? OwnerNpcId { get; set; }

    public long? KindId { get; set; }
}
