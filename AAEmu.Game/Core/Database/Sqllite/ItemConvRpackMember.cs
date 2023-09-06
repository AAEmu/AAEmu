using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemConvRpackMember
{
    public long? Id { get; set; }

    public long? ItemConvId { get; set; }

    public long? ItemConvRpackId { get; set; }
}
