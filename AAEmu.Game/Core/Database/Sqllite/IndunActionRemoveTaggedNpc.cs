using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class IndunActionRemoveTaggedNpc
{
    public long? Id { get; set; }

    public long? TagId { get; set; }
}
