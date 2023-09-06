using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFamily
{
    public long? Id { get; set; }

    public long? FamilyId { get; set; }

    public string Comment { get; set; }
}
