using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncTimer
{
    public long? Id { get; set; }

    public long? Delay { get; set; }

    public long? NextPhase { get; set; }

    public byte[] KeepRequester { get; set; }

    public byte[] ShowTip { get; set; }

    public byte[] ShowEndTime { get; set; }

    public string Tip { get; set; }
}
