using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncSpawnMgmt
{
    public long? Id { get; set; }

    public long? GroupId { get; set; }

    public byte[] Spawn { get; set; }

    public long? ZoneId { get; set; }
}
