using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// Earned titles
/// </summary>
public partial class Appellation
{
    public uint Id { get; set; }

    public bool Active { get; set; }

    public uint Owner { get; set; }
}
