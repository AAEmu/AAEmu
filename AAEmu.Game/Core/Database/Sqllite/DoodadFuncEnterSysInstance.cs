using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncEnterSysInstance
{
    public long? Id { get; set; }

    public long? ZoneId { get; set; }

    public long? FactionId { get; set; }

    public byte[] Selective { get; set; }
}
