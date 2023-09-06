using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class IndunAction
{
    public long? Id { get; set; }

    public long? ZoneGroupId { get; set; }

    public string Name { get; set; }

    public long? DetailId { get; set; }

    public string DetailType { get; set; }

    public string NextActionId { get; set; }
}
