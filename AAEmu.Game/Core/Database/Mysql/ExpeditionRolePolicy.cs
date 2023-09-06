using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// Guild role settings
/// </summary>
public partial class ExpeditionRolePolicy
{
    public int ExpeditionId { get; set; }

    public byte Role { get; set; }

    public string Name { get; set; }

    public bool DominionDeclare { get; set; }

    public bool Invite { get; set; }

    public bool Expel { get; set; }

    public bool Promote { get; set; }

    public bool Dismiss { get; set; }

    public bool Chat { get; set; }

    public bool ManagerChat { get; set; }

    public bool SiegeMaster { get; set; }

    public bool JoinSiege { get; set; }
}
