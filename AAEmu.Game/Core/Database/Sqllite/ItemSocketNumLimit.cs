using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemSocketNumLimit
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? SlotId { get; set; }

    public long? GradeId { get; set; }

    public long? NumSocket { get; set; }
}
