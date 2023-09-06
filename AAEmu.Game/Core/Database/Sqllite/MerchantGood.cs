using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class MerchantGood
{
    public long? Id { get; set; }

    public long? MerchantPackId { get; set; }

    public long? ItemId { get; set; }

    public long? GradeId { get; set; }
}
