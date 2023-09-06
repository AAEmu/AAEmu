using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadModifier
{
    public long? Id { get; set; }

    public long? OwnerId { get; set; }

    public string OwnerType { get; set; }

    public long? DoodadId { get; set; }

    public long? TagId { get; set; }

    public long? DoodadAttributeId { get; set; }

    public long? UnitModifierTypeId { get; set; }

    public long? Value { get; set; }
}
