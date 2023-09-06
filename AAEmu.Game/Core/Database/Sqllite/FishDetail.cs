using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class FishDetail
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? ItemId { get; set; }

    public long? MinWeight { get; set; }

    public long? MaxWeight { get; set; }

    public long? MinLength { get; set; }

    public long? MaxLength { get; set; }
}
