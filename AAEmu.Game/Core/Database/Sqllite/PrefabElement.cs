using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class PrefabElement
{
    public long? Id { get; set; }

    public long? PrefabModelId { get; set; }

    public long? StateId { get; set; }

    public string FilePath { get; set; }
}
