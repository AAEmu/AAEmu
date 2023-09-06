using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SkillProduct
{
    public long? Id { get; set; }

    public long? SkillId { get; set; }

    public long? ItemId { get; set; }

    public long? Amount { get; set; }
}
