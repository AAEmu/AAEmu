using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class BuffBreaker
{
    public long? Id { get; set; }

    public long? BuffId { get; set; }

    public long? BuffTagId { get; set; }
}
