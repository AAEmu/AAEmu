using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// Account specific values not related to login
/// </summary>
public partial class Account
{
    public int AccountId { get; set; }

    public int Credits { get; set; }
}
