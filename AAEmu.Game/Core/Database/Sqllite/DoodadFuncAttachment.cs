using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncAttachment
{
    public long? Id { get; set; }

    public long? AttachPointId { get; set; }

    public long? Space { get; set; }

    public long? BondKindId { get; set; }
}
