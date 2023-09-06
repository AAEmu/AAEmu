using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Zone
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? ZoneKey { get; set; }

    public long? GroupId { get; set; }

    public byte[] Closed { get; set; }

    public string DisplayText { get; set; }

    public long? FactionId { get; set; }

    public long? ZoneClimateId { get; set; }

    public byte[] AboxShow { get; set; }
}
