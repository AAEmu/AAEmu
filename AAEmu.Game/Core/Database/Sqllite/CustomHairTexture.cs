using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class CustomHairTexture
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public string DiffuseTexture { get; set; }

    public string SpecularTexture { get; set; }

    public string NormalTexture { get; set; }
}
