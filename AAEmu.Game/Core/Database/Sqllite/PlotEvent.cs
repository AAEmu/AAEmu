using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class PlotEvent
{
    public long? Id { get; set; }

    public long? PlotId { get; set; }

    public long? Position { get; set; }

    public string Name { get; set; }

    public long? SourceUpdateMethodId { get; set; }

    public long? TargetUpdateMethodId { get; set; }

    public long? TargetUpdateMethodParam1 { get; set; }

    public long? TargetUpdateMethodParam2 { get; set; }

    public long? TargetUpdateMethodParam3 { get; set; }

    public long? Tickets { get; set; }

    public long? TargetUpdateMethodParam4 { get; set; }

    public long? TargetUpdateMethodParam5 { get; set; }

    public long? TargetUpdateMethodParam6 { get; set; }

    public long? TargetUpdateMethodParam7 { get; set; }

    public byte[] AoeDiminishing { get; set; }

    public long? TargetUpdateMethodParam8 { get; set; }

    public long? TargetUpdateMethodParam9 { get; set; }
}
