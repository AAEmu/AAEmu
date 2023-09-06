using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Appellation
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? BuffId { get; set; }
}
