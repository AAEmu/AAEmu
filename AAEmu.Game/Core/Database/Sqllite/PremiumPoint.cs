using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class PremiumPoint
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? PremiumId { get; set; }

    public long? Time { get; set; }

    public long? Grade { get; set; }

    public long? SellType { get; set; }

    public long? Money { get; set; }
}
