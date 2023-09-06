using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class WorldGroup
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? TargetId { get; set; }

    public long? ImageMap { get; set; }

    public long? X { get; set; }

    public long? Y { get; set; }

    public long? W { get; set; }

    public long? H { get; set; }

    public long? ImageX { get; set; }

    public long? ImageY { get; set; }

    public long? ImageW { get; set; }

    public long? ImageH { get; set; }
}
