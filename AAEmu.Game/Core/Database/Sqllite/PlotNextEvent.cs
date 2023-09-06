using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class PlotNextEvent
{
    public long? Id { get; set; }

    public long? EventId { get; set; }

    public long? Position { get; set; }

    public long? NextEventId { get; set; }

    public byte[] PerTarget { get; set; }

    public byte[] Casting { get; set; }

    public long? Delay { get; set; }

    public long? Speed { get; set; }

    public byte[] Channeling { get; set; }

    public long? CastingInc { get; set; }

    public byte[] AddAnimCsTime { get; set; }

    public byte[] CastingDelayable { get; set; }

    public byte[] CastingCancelable { get; set; }

    public byte[] CancelOnBigHit { get; set; }

    public byte[] UseExeTime { get; set; }

    public byte[] Fail { get; set; }
}
