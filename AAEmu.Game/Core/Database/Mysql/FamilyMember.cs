using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// Family members
/// </summary>
public partial class FamilyMember
{
    public int CharacterId { get; set; }

    public int FamilyId { get; set; }

    public string Name { get; set; }

    public bool Role { get; set; }

    public string Title { get; set; }
}
