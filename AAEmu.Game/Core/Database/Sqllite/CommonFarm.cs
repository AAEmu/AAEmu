using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class CommonFarm
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? GuardTime { get; set; }

    public long? FarmGroupId { get; set; }

    public string Comments { get; set; }
}
