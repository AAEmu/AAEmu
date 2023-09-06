using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// Vocations
/// </summary>
public partial class Actability
{
    public uint Id { get; set; }

    public uint Point { get; set; }

    public byte Step { get; set; }

    public uint Owner { get; set; }
}
