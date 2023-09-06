using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class NpcInitialBuff
{
    public long? Id { get; set; }

    public long? NpcId { get; set; }

    public long? BuffId { get; set; }
}
