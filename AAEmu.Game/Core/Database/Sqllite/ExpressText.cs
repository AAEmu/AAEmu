using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ExpressText
{
    public long? Id { get; set; }

    public long? AnimId { get; set; }

    public string Me { get; set; }

    public string MeTarget { get; set; }

    public string Other { get; set; }

    public string OtherTarget { get; set; }

    public string OtherMe { get; set; }

    public long? NpcAnimId { get; set; }
}
