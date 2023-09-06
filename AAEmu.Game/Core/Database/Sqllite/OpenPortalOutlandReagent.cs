using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class OpenPortalOutlandReagent
{
    public long? Id { get; set; }

    public long? OpenPortalEffectId { get; set; }

    public long? ItemId { get; set; }

    public long? Amount { get; set; }

    public long? Priority { get; set; }
}
