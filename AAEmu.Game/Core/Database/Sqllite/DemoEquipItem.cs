using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DemoEquipItem
{
    public long? Id { get; set; }

    public long? DemoEquipId { get; set; }

    public long? ItemId { get; set; }
}
