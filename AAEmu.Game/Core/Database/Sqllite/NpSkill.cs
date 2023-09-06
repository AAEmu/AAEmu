using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class NpSkill
{
    public long? Id { get; set; }

    public long? OwnerId { get; set; }

    public string OwnerType { get; set; }

    public long? SkillId { get; set; }

    public long? SkillUseConditionId { get; set; }

    public double? SkillUseParam1 { get; set; }

    public double? SkillUseParam2 { get; set; }
}
