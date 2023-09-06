using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class PlotEffect
{
    public long? Id { get; set; }

    public long? EventId { get; set; }

    public long? Position { get; set; }

    public long? SourceId { get; set; }

    public long? TargetId { get; set; }

    public long? ActualId { get; set; }

    public string ActualType { get; set; }
}
