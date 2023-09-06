using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadBundleDoodad
{
    public long? Id { get; set; }

    public long? DoodadId { get; set; }

    public long? DoodadBundleId { get; set; }
}
