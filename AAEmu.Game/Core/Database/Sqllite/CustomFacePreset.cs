using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class CustomFacePreset
{
    public long? Id { get; set; }

    public long? ModelId { get; set; }

    public long? FaceMorphTypeId { get; set; }

    public byte[] Modifier { get; set; }

    public string Name { get; set; }
}
