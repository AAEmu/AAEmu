using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Taxation
{
    public long? Id { get; set; }

    public long? Tax { get; set; }

    public string Desc { get; set; }

    public byte[] Show { get; set; }
}
