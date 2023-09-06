using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ExpandExpertLimit
{
    public long? Id { get; set; }

    public long? ExpandCount { get; set; }

    public long? LifePoint { get; set; }

    public long? ItemId { get; set; }

    public long? ItemCount { get; set; }
}
