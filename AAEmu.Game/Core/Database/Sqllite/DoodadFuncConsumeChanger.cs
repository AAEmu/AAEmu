using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncConsumeChanger
{
    public long? Id { get; set; }

    public long? SlotId { get; set; }

    public long? Count { get; set; }
}
