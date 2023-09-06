using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class CraftProduct
{
    public long? Id { get; set; }

    public long? CraftId { get; set; }

    public long? ItemId { get; set; }

    public long? Amount { get; set; }

    public long? Rate { get; set; }

    public byte[] ShowLowerCrafts { get; set; }

    public byte[] UseGrade { get; set; }

    public long? ItemGradeId { get; set; }
}
