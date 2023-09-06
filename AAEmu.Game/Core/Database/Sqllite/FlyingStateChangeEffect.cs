using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class FlyingStateChangeEffect
{
    public long? Id { get; set; }

    public byte[] FlyingState { get; set; }
}
