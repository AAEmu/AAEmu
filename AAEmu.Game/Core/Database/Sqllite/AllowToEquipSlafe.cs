using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class AllowToEquipSlafe
{
    public long? Id { get; set; }

    public long? SlaveEquipPackId { get; set; }

    public long? SlaveId { get; set; }
}
