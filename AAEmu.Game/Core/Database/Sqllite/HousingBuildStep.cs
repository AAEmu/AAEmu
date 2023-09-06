using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class HousingBuildStep
{
    public long? Id { get; set; }

    public long? HousingId { get; set; }

    public long? Step { get; set; }

    public long? ModelId { get; set; }

    public long? SkillId { get; set; }

    public long? NumActions { get; set; }
}
