using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class EquipPackWeapon
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? MainhandId { get; set; }

    public long? OffhandId { get; set; }

    public long? RangedId { get; set; }

    public long? MusicalId { get; set; }

    public long? MainhandGradeId { get; set; }

    public long? OffhandGradeId { get; set; }

    public long? RangedGradeId { get; set; }

    public long? MusicalGradeId { get; set; }
}
