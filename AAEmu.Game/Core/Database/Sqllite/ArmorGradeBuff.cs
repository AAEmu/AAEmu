using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ArmorGradeBuff
{
    public long? Id { get; set; }

    public long? ArmorTypeId { get; set; }

    public long? ItemGradeId { get; set; }

    public long? BuffId { get; set; }
}
