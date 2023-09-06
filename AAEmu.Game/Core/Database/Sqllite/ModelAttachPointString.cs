using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ModelAttachPointString
{
    public long? Id { get; set; }

    public string Actor { get; set; }

    public string Prefab { get; set; }
}
