using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// Recorded house portals in the portal book
/// </summary>
public partial class PortalBookCoord
{
    public int Id { get; set; }

    public string Name { get; set; }

    public int? X { get; set; }

    public int? Y { get; set; }

    public int? Z { get; set; }

    public int? ZoneId { get; set; }

    public int? ZRot { get; set; }

    public int? SubZoneId { get; set; }

    public int Owner { get; set; }
}
