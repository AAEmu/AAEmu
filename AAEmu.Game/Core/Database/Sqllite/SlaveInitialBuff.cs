using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SlaveInitialBuff
{
    public long? Id { get; set; }

    public long? SlaveId { get; set; }

    public long? BuffId { get; set; }
}
