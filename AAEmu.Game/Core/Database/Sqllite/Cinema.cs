using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Cinema
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public byte[] Replay { get; set; }
}
