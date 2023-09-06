using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class MouldPackItem
{
    public long? Id { get; set; }

    public long? MouldPackId { get; set; }

    public long? MouldId { get; set; }
}
