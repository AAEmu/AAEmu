using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

public partial class Blocked
{
    public int Owner { get; set; }

    public int BlockedId { get; set; }
}
