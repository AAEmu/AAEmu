using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemProc
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    public long? SkillId { get; set; }

    public long? ChanceKindId { get; set; }

    public long? ChanceRate { get; set; }

    public long? ChanceParam { get; set; }

    public long? CooldownSec { get; set; }

    public byte[] Finisher { get; set; }

    public long? ItemLevelBasedChanceBonus { get; set; }
}
