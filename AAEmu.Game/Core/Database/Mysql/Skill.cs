using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// Learned character skills
/// </summary>
public partial class Skill
{
    public uint Id { get; set; }

    public sbyte Level { get; set; }

    public string Type { get; set; }

    public uint Owner { get; set; }
}
