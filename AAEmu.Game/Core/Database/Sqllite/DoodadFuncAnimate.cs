using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncAnimate
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public byte[] PlayOnce { get; set; }
}
