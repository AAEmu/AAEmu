using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SiegeTicketOffensePrice
{
    public long? Id { get; set; }

    public long? Count { get; set; }

    public long? PerPrice { get; set; }
}
