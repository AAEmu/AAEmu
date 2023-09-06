using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Tag
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public string Desc { get; set; }

    public byte[] Translate { get; set; }
}
