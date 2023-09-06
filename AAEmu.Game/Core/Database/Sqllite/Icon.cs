using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Icon
{
    public long? Id { get; set; }

    public string Filename { get; set; }

    public string Name { get; set; }
}
