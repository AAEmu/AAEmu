using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ChangeEquipmentBuff
{
    public long? Id { get; set; }

    public long? OwnerId { get; set; }

    public string OwnerType { get; set; }

    public long? BuffId { get; set; }
}
