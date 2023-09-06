using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// List of visited area for the portal book
/// </summary>
public partial class PortalVisitedDistrict
{
    public int Id { get; set; }

    public int Subzone { get; set; }

    public int Owner { get; set; }
}
