using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Effect
{
    public long? Id { get; set; }

    public long? ActualId { get; set; }

    public string ActualType { get; set; }
}
