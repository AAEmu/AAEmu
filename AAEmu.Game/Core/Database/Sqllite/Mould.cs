using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Mould
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? CraftId { get; set; }

    public long? Delay { get; set; }
}
