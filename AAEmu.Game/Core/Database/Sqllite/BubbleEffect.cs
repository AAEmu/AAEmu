using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class BubbleEffect
{
    public long? Id { get; set; }

    public long? KindId { get; set; }

    public string Speech { get; set; }
}
