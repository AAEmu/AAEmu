using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SkillSynergyIcon
{
    public long? Id { get; set; }

    public long? SkillId { get; set; }

    public string Desc { get; set; }

    public byte[] ConditionBuffkind { get; set; }

    public long? ConditionIconId { get; set; }

    public byte[] ResultBuffkind { get; set; }

    public long? ResultIconId { get; set; }

    public long? UnitSelectionId { get; set; }

    public long? BuffTagId { get; set; }

    public string WebDesc { get; set; }
}
