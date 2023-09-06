using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncHousingArea
{
    public long? Id { get; set; }

    public long? FactionId { get; set; }

    public long? Radius { get; set; }
}
