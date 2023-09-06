using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncPulseTrigger
{
    public long? Id { get; set; }

    public byte[] Flag { get; set; }

    public long? NextPhase { get; set; }
}
