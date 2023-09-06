using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class CurrencyConfig
{
    public long? Id { get; set; }

    public long? KindId { get; set; }

    public long? CurrencyId { get; set; }
}
