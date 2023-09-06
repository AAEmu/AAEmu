using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// Skillsets Exp
/// </summary>
public partial class Ability
{
    public byte Id { get; set; }

    public int Exp { get; set; }

    public uint Owner { get; set; }
}
