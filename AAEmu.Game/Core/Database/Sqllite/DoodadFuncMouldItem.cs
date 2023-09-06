using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncMouldItem
{
    public long? Id { get; set; }

    public long? DoodadFuncMouldId { get; set; }

    public long? MouldPackId { get; set; }
}
