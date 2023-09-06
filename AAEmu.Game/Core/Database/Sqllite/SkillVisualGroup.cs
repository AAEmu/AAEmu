using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SkillVisualGroup
{
    public long? Id { get; set; }

    public long? OwnerId { get; set; }

    public string OwnerType { get; set; }

    public long? Level { get; set; }

    public long? FxGroupId { get; set; }

    public long? ProjectileId { get; set; }
}
