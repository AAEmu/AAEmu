using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemConvPpackMember
{
    public long? Id { get; set; }

    public long? ItemConvId { get; set; }

    public long? ItemConvPpackId { get; set; }
}
