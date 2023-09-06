using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Demo
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public byte[] AddExp { get; set; }
}
