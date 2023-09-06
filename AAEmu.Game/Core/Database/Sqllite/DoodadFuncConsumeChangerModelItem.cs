using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncConsumeChangerModelItem
{
    public long? Id { get; set; }

    public long? DoodadFuncConsumeChangerModelId { get; set; }

    public long? ItemId { get; set; }

    public string Model { get; set; }
}
