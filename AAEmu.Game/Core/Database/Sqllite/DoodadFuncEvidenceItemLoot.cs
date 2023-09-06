using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncEvidenceItemLoot
{
    public long? Id { get; set; }

    public long? SkillId { get; set; }

    public long? CrimeValue { get; set; }

    public long? CrimeKindId { get; set; }
}
