using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncPuzzleIn
{
    public long? Id { get; set; }

    public long? GroupId { get; set; }

    public double? Ratio { get; set; }

    public string Model { get; set; }
}
