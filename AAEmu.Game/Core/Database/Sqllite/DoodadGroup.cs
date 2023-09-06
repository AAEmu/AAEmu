using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadGroup
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public byte[] IsExport { get; set; }

    public long? GuardOnFieldTime { get; set; }

    public byte[] RemovedByHouse { get; set; }
}
