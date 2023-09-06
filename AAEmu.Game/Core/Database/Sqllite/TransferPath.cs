using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class TransferPath
{
    public long? Id { get; set; }

    public long? OwnerId { get; set; }

    public string OwnerType { get; set; }

    public string PathName { get; set; }

    public double? WaitTimeStart { get; set; }

    public double? WaitTimeEnd { get; set; }
}
