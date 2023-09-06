using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncSiegePeriod
{
    public long? Id { get; set; }

    public long? SiegePeriodId { get; set; }

    public long? NextPhase { get; set; }

    public byte[] Defense { get; set; }
}
