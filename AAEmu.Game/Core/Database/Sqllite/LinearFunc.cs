using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class LinearFunc
{
    public long? Id { get; set; }

    public long? StartValue { get; set; }

    public long? EndValue { get; set; }
}
