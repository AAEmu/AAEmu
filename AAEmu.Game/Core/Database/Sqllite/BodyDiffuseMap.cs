using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class BodyDiffuseMap
{
    public long? Id { get; set; }

    public long? ModelId { get; set; }

    public string Name { get; set; }

    public string Diffuse { get; set; }
}
