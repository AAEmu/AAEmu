using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class TowerDefProg
{
    public long? Id { get; set; }

    public long? TowerDefId { get; set; }

    public string Msg { get; set; }

    public double? CondToNextTime { get; set; }

    public byte[] CondCompByAnd { get; set; }
}
