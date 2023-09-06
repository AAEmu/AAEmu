using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class PriestBuff
{
    public long? Id { get; set; }

    public long? BuffId { get; set; }

    public long? Cost { get; set; }

    public long? Position { get; set; }
}
