using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DyeingColor
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? ColorRgb { get; set; }
}
