using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// Guilds
/// </summary>
public partial class Expedition
{
    public int Id { get; set; }

    public int Owner { get; set; }

    public string OwnerName { get; set; }

    public string Name { get; set; }

    public int Mother { get; set; }

    public DateTime CreatedAt { get; set; }
}
