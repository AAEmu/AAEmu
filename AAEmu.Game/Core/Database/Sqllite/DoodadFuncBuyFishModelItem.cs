using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncBuyFishModelItem
{
    public long? Id { get; set; }

    public long? DoodadFuncBuyFishModelId { get; set; }

    public long? ItemId { get; set; }

    public string Model { get; set; }
}
