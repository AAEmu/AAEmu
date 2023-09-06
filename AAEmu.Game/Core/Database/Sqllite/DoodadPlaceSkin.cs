using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadPlaceSkin
{
    public long? Id { get; set; }

    public long? DoodadAlmightyId { get; set; }

    public long? DoodadPlaceSkinKindId { get; set; }
}
