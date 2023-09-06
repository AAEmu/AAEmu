using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class CharRecord
{
    public long? Id { get; set; }

    public long? KindId { get; set; }

    public long? Value1 { get; set; }

    public long? Value2 { get; set; }
}
